using System.ComponentModel.DataAnnotations;
using Gym_Management.Data;
using Gym_Management.Domain;
using Gym_Management.Services;
using Gym_Management.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gym_Management.Controllers;

public class CreateSubscriptionRequest
{
    [NotEmptyGuid(ErrorMessage = "customerId must be a non-empty GUID.")]
    public Guid CustomerId { get; set; }

    [NotEmptyGuid(ErrorMessage = "planId must be a non-empty GUID.")]
    public Guid PlanId { get; set; }

    [Required(ErrorMessage = "startDate is required (yyyy-MM-dd).")]
    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    /// <summary>Required for Session plans — how many sessions this customer bought.</summary>
    [Range(1, 10000, ErrorMessage = "totalSessions must be between 1 and 10000 when provided.")]
    public int? TotalSessions { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "overridePrice must be between 0 and 1000000 when provided.")]
    public decimal? OverridePrice { get; set; }
}

public class CancelSubscriptionRequest
{
    [NotBlank(ErrorMessage = "reason is required and cannot be blank.")]
    [MaxLength(500, ErrorMessage = "reason must be at most 500 characters.")]
    public string Reason { get; set; } = string.Empty;
}

public record SubscriptionDto(
    Guid Id,
    Guid CustomerId,
    Guid PlanId,
    string Type,
    string Status,
    DateOnly StartDate,
    DateOnly? EndDate,
    int? TotalSessions,
    int UsedSessions,
    int? RemainingSessions,
    decimal? PricePaid,
    DateTime CreatedAtUtc);

/// <summary>Sell, renew, cancel, and list subscriptions.</summary>
[ApiController]
[Authorize]
[Tags("Subscriptions")]
[Route("api/subscriptions")]
public class SubscriptionsController(
    GymDbContext db,
    IGymClock clock,
    IAuditService audit) : ControllerBase
{
    /// <summary>List subscriptions (optional <c>customerId</c> / derived <c>status</c> filter).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionDto>>> List(
        [FromQuery] Guid? customerId,
        [FromQuery] string? status,
        CancellationToken cancellationToken = default)
    {
        var today = await clock.TodayAsync(cancellationToken);
        var q = db.Subscriptions.AsNoTracking().AsQueryable();
        if (customerId is not null)
        {
            q = q.Where(s => s.CustomerId == customerId);
        }

        var items = await q.OrderByDescending(s => s.CreatedAtUtc).ToListAsync(cancellationToken);
        var dtos = items.Select(s => ToDto(s, today)).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim();
            dtos = dtos.Where(d => d.Status.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(dtos.ToList());
    }

    /// <summary>Create a subscription for a customer.</summary>
    /// <remarks>
    /// <b>Time plan:</b> <c>endDate</c> defaults to start + durationDays − 1. Do not send <c>totalSessions</c>.
    /// <b>Session plan:</b> send <c>totalSessions</c> (how many credits this sale includes). No <c>endDate</c>.
    /// Price defaults to the plan price; override with <c>overridePrice</c> if needed.
    /// Overlap with a live sub → <c>409 overlap_conflict</c>, except a renewal starting the day after a time plan’s end.
    /// A live session sub blocks everything until cancelled.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubscriptionDto>> Create(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var staffId = StaffContext.GetStaffId(User);
        var today = await clock.TodayAsync(cancellationToken);

        _ = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw ApiErrors.NotFound("Customer not found.");

        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken)
            ?? throw ApiErrors.NotFound("Plan not found.");

        if (!plan.IsActive)
        {
            throw ApiErrors.Validation("Plan is not active.");
        }

        DateOnly? endDate = null;
        int? totalSessions = null;

        if (plan.Type == PlanType.Time)
        {
            if (request.TotalSessions is not null)
            {
                throw ApiErrors.Validation("Time subscriptions must not include totalSessions.");
            }

            endDate = request.EndDate ?? request.StartDate.AddDays((plan.DurationDays ?? 1) - 1);
            if (endDate < request.StartDate)
            {
                throw ApiErrors.Validation("endDate must be on or after startDate.");
            }
        }
        else
        {
            if (request.EndDate is not null)
            {
                throw ApiErrors.Validation("Session subscriptions must not have an endDate.");
            }

            if (request.TotalSessions is null or < 1)
            {
                throw ApiErrors.Validation("Session subscriptions require totalSessions >= 1 (chosen per customer, not on the plan).");
            }

            totalSessions = request.TotalSessions;
        }

        var existing = await db.Subscriptions
            .Where(s => s.CustomerId == request.CustomerId)
            .ToListAsync(cancellationToken);

        var conflict = SubscriptionRules.ValidateNewSubscription(existing, request.StartDate, today);
        if (conflict is not null)
        {
            throw ApiErrors.OverlapConflict(conflict);
        }

        var sub = new Subscription
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            PlanId = plan.Id,
            Type = plan.Type == PlanType.Time ? SubscriptionType.Time : SubscriptionType.Session,
            StartDate = request.StartDate,
            EndDate = endDate,
            TotalSessions = totalSessions,
            UsedSessions = 0,
            PricePaid = request.OverridePrice ?? plan.Price,
            CreatedAtUtc = clock.UtcNow,
            CreatedByStaffId = staffId
        };

        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(sub, today));
    }

    /// <summary>Cancel a subscription (reason required). Writes an audit log.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubscriptionDto>> Cancel(
        Guid id,
        CancelSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var staffId = StaffContext.GetStaffId(User);
        var today = await clock.TodayAsync(cancellationToken);

        var sub = await db.Subscriptions.SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw ApiErrors.NotFound("Subscription not found.");

        if (sub.CancelledAtUtc is not null)
        {
            throw ApiErrors.Validation("Subscription is already cancelled.");
        }

        sub.CancelledAtUtc = clock.UtcNow;
        sub.CancelReason = request.Reason.Trim();
        await db.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            staffId,
            "SubscriptionCancel",
            "Subscription",
            sub.Id.ToString(),
            sub.CancelReason,
            cancellationToken);

        return Ok(ToDto(sub, today));
    }

    private static SubscriptionDto ToDto(Subscription s, DateOnly today)
    {
        var status = SubscriptionStatus.Derive(s, today);
        int? remaining = s.Type == SubscriptionType.Session
            ? Math.Max(0, (s.TotalSessions ?? 0) - s.UsedSessions)
            : null;
        return new SubscriptionDto(
            s.Id,
            s.CustomerId,
            s.PlanId,
            s.Type.ToString(),
            SubscriptionStatus.ToApiString(status),
            s.StartDate,
            s.EndDate,
            s.TotalSessions,
            s.UsedSessions,
            remaining,
            s.PricePaid,
            s.CreatedAtUtc);
    }
}
