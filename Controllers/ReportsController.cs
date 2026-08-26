using Gym_Management.Data;
using Gym_Management.Domain;
using Gym_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gym_Management.Controllers;

public record LowBalanceItemDto(
    Guid CustomerId,
    string CustomerName,
    Guid SubscriptionId,
    int RemainingSessions);

public record DashboardDto(
    int TodayGranted,
    int TodayDenied,
    int ActiveSubscriptions,
    int ExpiredSubscriptions,
    int ExhaustedSubscriptions,
    IReadOnlyList<LowBalanceItemDto> LowBalance);

/// <summary>Dashboard counts for the staff home screen.</summary>
[ApiController]
[Authorize]
[Tags("Reports")]
[Route("api/reports")]
public class ReportsController(
    GymDbContext db,
    ISettingsService settings,
    IGymClock clock) : ControllerBase
{
    /// <summary>Today’s check-ins, sub counts, and low session balance list.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDto>> Dashboard(CancellationToken cancellationToken)
    {
        var today = await clock.TodayAsync(cancellationToken);
        var timezoneId = await settings.GetTimezoneIdAsync(cancellationToken);
        var dayStartUtc = StartOfLocalDayUtc(today, timezoneId);
        var dayEndUtc = StartOfLocalDayUtc(today.AddDays(1), timezoneId);

        var todayGranted = await db.AttendanceLogs.AsNoTracking()
            .CountAsync(a => a.AtUtc >= dayStartUtc && a.AtUtc < dayEndUtc && a.Result == AttendanceResult.Granted, cancellationToken);
        var todayDenied = await db.AttendanceLogs.AsNoTracking()
            .CountAsync(a => a.AtUtc >= dayStartUtc && a.AtUtc < dayEndUtc && a.Result == AttendanceResult.Denied, cancellationToken);

        var subs = await db.Subscriptions.AsNoTracking().ToListAsync(cancellationToken);
        var active = 0;
        var expired = 0;
        var exhausted = 0;
        foreach (var s in subs)
        {
            switch (SubscriptionStatus.Derive(s, today))
            {
                case DerivedSubscriptionStatus.Active:
                    active++;
                    break;
                case DerivedSubscriptionStatus.Expired:
                    expired++;
                    break;
                case DerivedSubscriptionStatus.Exhausted:
                    exhausted++;
                    break;
            }
        }

        var threshold = await settings.GetLowBalanceThresholdAsync(cancellationToken);
        var lowSessionSubs = subs
            .Where(s =>
                s.Type == SubscriptionType.Session &&
                SubscriptionStatus.Derive(s, today) == DerivedSubscriptionStatus.Active &&
                ((s.TotalSessions ?? 0) - s.UsedSessions) < threshold)
            .ToList();

        var customerIds = lowSessionSubs.Select(s => s.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var lowBalance = lowSessionSubs
            .Select(s =>
            {
                customers.TryGetValue(s.CustomerId, out var c);
                var name = c is null ? "" : $"{c.FirstName} {c.LastName}".Trim();
                return new LowBalanceItemDto(
                    s.CustomerId,
                    name,
                    s.Id,
                    Math.Max(0, (s.TotalSessions ?? 0) - s.UsedSessions));
            })
            .OrderBy(x => x.RemainingSessions)
            .ToList();

        return Ok(new DashboardDto(todayGranted, todayDenied, active, expired, exhausted, lowBalance));
    }

    private static DateTime StartOfLocalDayUtc(DateOnly localDate, string timezoneId)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch
        {
            zone = TimeZoneInfo.Utc;
        }

        var local = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
