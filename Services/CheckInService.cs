using System.Diagnostics;
using Gym_Management.Data;
using Gym_Management.Domain;
using Gym_Management.Observability;
using Microsoft.EntityFrameworkCore;

namespace Gym_Management.Services;

public record CheckInCustomerDto(string FullName);

public record CheckInSubscriptionDto(
    string Type,
    string Status,
    int? RemainingSessions,
    DateOnly? EndDate);

public record CheckInResultDto(
    string Result,
    string? Reason,
    CheckInCustomerDto? Customer,
    CheckInSubscriptionDto? Subscription);

public interface ICheckInService
{
    Task<CheckInResultDto> CheckInAsync(string rawToken, Guid staffId, CancellationToken cancellationToken = default);
}

public class CheckInService(
    GymDbContext db,
    IQrTokenService qrTokens,
    ISettingsService settings,
    IGymClock clock,
    ILogger<CheckInService> logger) : ICheckInService
{
    public async Task<CheckInResultDto> CheckInAsync(string rawToken, Guid staffId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw ApiErrors.Validation("Token is required.");
        }

        using var activity = GymActivities.Source.StartActivity(GymActivities.Operations.CheckIn);
        activity?.SetTag("staff.id", staffId.ToString());

        var sw = Stopwatch.StartNew();
        var tokenHash = qrTokens.HashToken(rawToken.Trim());
        var customer = await db.Customers.AsNoTracking()
            .SingleOrDefaultAsync(c => c.TokenHash == tokenHash, cancellationToken);

        if (customer is null)
        {
            activity?.SetTag("checkin.result", "denied");
            activity?.SetTag("checkin.reason", "token_unknown");
            logger.LogInformation(
                GymLogEvents.CheckInDenied,
                "Check-in denied reason={Reason} staffId={StaffId} elapsedMs={ElapsedMs}",
                "token_unknown",
                staffId,
                sw.ElapsedMilliseconds);
            return Denied("token_unknown");
        }

        activity?.SetTag("customer.id", customer.Id.ToString());

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await AcquireCustomerLockAsync(customer.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                GymLogEvents.CheckInLockFailed,
                ex,
                "Check-in lock failed customerId={CustomerId} staffId={StaffId}",
                customer.Id,
                staffId);
            throw;
        }

        // Re-load tracked entity inside the transaction.
        customer = await db.Customers.SingleAsync(c => c.Id == customer.Id, cancellationToken);

        if (!customer.IsActive)
        {
            await LogDeniedAsync(customer.Id, null, staffId, "customer_inactive", null, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return FinishDenied("customer_inactive", customer, null, null, staffId, sw, activity);
        }

        var thresholdMinutes = await settings.GetDuplicateScanThresholdMinutesAsync(cancellationToken);
        var since = clock.UtcNow.AddMinutes(-thresholdMinutes);
        var duplicate = await db.AttendanceLogs.AsNoTracking()
            .AnyAsync(
                a => a.CustomerId == customer.Id
                     && a.Result == AttendanceResult.Granted
                     && a.AtUtc >= since,
                cancellationToken);

        if (duplicate)
        {
            await LogDeniedAsync(customer.Id, null, staffId, "duplicate_scan", null, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return FinishDenied("duplicate_scan", customer, null, null, staffId, sw, activity);
        }

        var today = await clock.TodayAsync(cancellationToken);
        var subs = await db.Subscriptions
            .Where(s => s.CustomerId == customer.Id)
            .ToListAsync(cancellationToken);

        var resolution = ResolveSubscription(subs, today);
        if (resolution.DenyReason is not null)
        {
            await LogDeniedAsync(customer.Id, resolution.Subscription?.Id, staffId, resolution.DenyReason, null, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return FinishDenied(resolution.DenyReason, customer, resolution.Subscription, today, staffId, sw, activity);
        }

        var sub = resolution.Subscription!;
        int? remainingAfter = null;

        if (sub.Type == SubscriptionType.Session)
        {
            var rows = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE Subscriptions
                 SET UsedSessions = UsedSessions + 1
                 WHERE Id = {sub.Id} AND UsedSessions < TotalSessions
                 """,
                cancellationToken);

            if (rows == 0)
            {
                await LogDeniedAsync(customer.Id, sub.Id, staffId, "no_sessions_remaining", null, cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return FinishDenied("no_sessions_remaining", customer, sub, today, staffId, sw, activity);
            }

            await db.Entry(sub).ReloadAsync(cancellationToken);
            remainingAfter = (sub.TotalSessions ?? 0) - sub.UsedSessions;
        }

        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            SubscriptionId = sub.Id,
            StaffId = staffId,
            AtUtc = clock.UtcNow,
            Result = AttendanceResult.Granted,
            Reason = null,
            RemainingSessionsAfter = remainingAfter
        });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var status = SubscriptionStatus.Derive(sub, today);
        activity?.SetTag("checkin.result", "granted");
        activity?.SetTag("subscription.id", sub.Id.ToString());
        activity?.SetTag("subscription.type", sub.Type.ToString());
        logger.LogInformation(
            GymLogEvents.CheckInGranted,
            "Check-in granted customerId={CustomerId} subscriptionId={SubscriptionId} type={Type} staffId={StaffId} remainingSessions={RemainingSessions} elapsedMs={ElapsedMs}",
            customer.Id,
            sub.Id,
            sub.Type,
            staffId,
            remainingAfter,
            sw.ElapsedMilliseconds);

        return new CheckInResultDto(
            "granted",
            null,
            new CheckInCustomerDto($"{customer.FirstName} {customer.LastName}".Trim()),
            new CheckInSubscriptionDto(
                sub.Type.ToString(),
                SubscriptionStatus.ToApiString(status),
                sub.Type == SubscriptionType.Session ? remainingAfter : null,
                sub.Type == SubscriptionType.Time ? sub.EndDate : null));
    }

    private CheckInResultDto FinishDenied(
        string reason,
        Customer customer,
        Subscription? sub,
        DateOnly? today,
        Guid staffId,
        Stopwatch sw,
        Activity? activity)
    {
        activity?.SetTag("checkin.result", "denied");
        activity?.SetTag("checkin.reason", reason);
        logger.LogInformation(
            GymLogEvents.CheckInDenied,
            "Check-in denied reason={Reason} customerId={CustomerId} staffId={StaffId} elapsedMs={ElapsedMs}",
            reason,
            customer.Id,
            staffId,
            sw.ElapsedMilliseconds);
        return Denied(reason, customer, sub, today);
    }

    private static CheckInResultDto Denied(
        string reason,
        Customer? customer = null,
        Subscription? sub = null,
        DateOnly? today = null)
    {
        CheckInCustomerDto? customerDto = customer is null
            ? null
            : new CheckInCustomerDto($"{customer.FirstName} {customer.LastName}".Trim());

        CheckInSubscriptionDto? subDto = null;
        if (sub is not null && today is not null)
        {
            var status = SubscriptionStatus.Derive(sub, today.Value);
            int? remaining = sub.Type == SubscriptionType.Session
                ? Math.Max(0, (sub.TotalSessions ?? 0) - sub.UsedSessions)
                : null;
            subDto = new CheckInSubscriptionDto(
                sub.Type.ToString(),
                SubscriptionStatus.ToApiString(status),
                remaining,
                sub.Type == SubscriptionType.Time ? sub.EndDate : null);
        }

        return new CheckInResultDto("denied", reason, customerDto, subDto);
    }

    private async Task LogDeniedAsync(
        Guid customerId,
        Guid? subscriptionId,
        Guid staffId,
        string reason,
        int? remaining,
        CancellationToken cancellationToken)
    {
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            SubscriptionId = subscriptionId,
            StaffId = staffId,
            AtUtc = clock.UtcNow,
            Result = AttendanceResult.Denied,
            Reason = reason,
            RemainingSessionsAfter = remaining
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AcquireCustomerLockAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var resource = customerId.ToString();
        var rows = await db.Database
            .SqlQuery<int>($"""
                DECLARE @result int;
                EXEC @result = sp_getapplock
                    @Resource = {resource},
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 5000;
                SELECT @result AS [Value];
                """)
            .ToListAsync(cancellationToken);

        var result = rows.FirstOrDefault();
        if (result < 0)
        {
            throw new InvalidOperationException($"Could not acquire customer lock (sp_getapplock={result}).");
        }
    }

    private static (Subscription? Subscription, string? DenyReason) ResolveSubscription(
        IReadOnlyList<Subscription> subs,
        DateOnly today)
    {
        var withStatus = subs
            .Select(s => (Sub: s, Status: SubscriptionStatus.Derive(s, today)))
            .ToList();

        var active = withStatus.FirstOrDefault(x => x.Status == DerivedSubscriptionStatus.Active);
        if (active.Sub is not null)
        {
            return (active.Sub, null);
        }

        if (withStatus.Any(x => x.Status == DerivedSubscriptionStatus.Scheduled))
        {
            return (withStatus.First(x => x.Status == DerivedSubscriptionStatus.Scheduled).Sub, "not_started");
        }

        if (withStatus.Any(x => x.Status == DerivedSubscriptionStatus.Exhausted))
        {
            return (withStatus.First(x => x.Status == DerivedSubscriptionStatus.Exhausted).Sub, "no_sessions_remaining");
        }

        if (withStatus.Any(x => x.Status == DerivedSubscriptionStatus.Expired))
        {
            return (withStatus.First(x => x.Status == DerivedSubscriptionStatus.Expired).Sub, "expired");
        }

        return (null, "no_active_subscription");
    }
}
