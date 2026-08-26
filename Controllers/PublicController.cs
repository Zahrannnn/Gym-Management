using System.Diagnostics;
using Gym_Management.Data;
using Gym_Management.Domain;
using Gym_Management.Observability;
using Gym_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Gym_Management.Controllers;

public record PublicStatusDto(
    string GymName,
    string CustomerName,
    string? SubscriptionType,
    string? Status,
    int? RemainingSessions,
    DateOnly? EndDate);

/// <summary>Customer self-service portal (no auth).</summary>
[ApiController]
[AllowAnonymous]
[Tags("Public")]
[Route("api/public")]
public class PublicController(
    GymDbContext db,
    IQrTokenService qrTokens,
    ISettingsService settings,
    IGymClock clock) : ControllerBase
{
    /// <summary>Read-only status for a QR token (Next.js portal).</summary>
    /// <remarks>
    /// Never deducts sessions or writes attendance.
    /// Unknown token → <c>404</c> with no details.
    /// Rate limit: 10 requests / minute / IP → <c>429 rate_limited</c>.
    /// Name format: <c>First L.</c> — no phone, ids, or staff data.
    /// </remarks>
    [HttpGet("status/{token}")]
    [EnableRateLimiting("public-status")]
    [ProducesResponseType(typeof(PublicStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PublicStatusDto>> Status(string token, CancellationToken cancellationToken)
    {
        using var activity = GymActivities.Source.StartActivity(GymActivities.Operations.PublicStatus);

        if (string.IsNullOrWhiteSpace(token))
        {
            activity?.SetTag("public.found", false);
            throw ApiErrors.NotFound();
        }

        var hash = qrTokens.HashToken(token.Trim());
        var customer = await db.Customers.AsNoTracking()
            .SingleOrDefaultAsync(c => c.TokenHash == hash, cancellationToken);

        if (customer is null)
        {
            activity?.SetTag("public.found", false);
            throw ApiErrors.NotFound();
        }

        activity?.SetTag("public.found", true);
        activity?.SetTag("customer.id", customer.Id.ToString());

        var today = await clock.TodayAsync(cancellationToken);
        var gymName = await settings.GetGymNameAsync(cancellationToken);
        var displayName = FormatPublicName(customer.FirstName, customer.LastName);

        var subs = await db.Subscriptions.AsNoTracking()
            .Where(s => s.CustomerId == customer.Id)
            .ToListAsync(cancellationToken);

        var chosen = PickPublicSubscription(subs, today);
        if (chosen is null)
        {
            return Ok(new PublicStatusDto(gymName, displayName, null, null, null, null));
        }

        var status = SubscriptionStatus.Derive(chosen, today);
        int? remaining = chosen.Type == SubscriptionType.Session
            ? Math.Max(0, (chosen.TotalSessions ?? 0) - chosen.UsedSessions)
            : null;

        return Ok(new PublicStatusDto(
            gymName,
            displayName,
            chosen.Type.ToString(),
            SubscriptionStatus.ToApiString(status),
            remaining,
            chosen.Type == SubscriptionType.Time ? chosen.EndDate : null));
    }

    private static string FormatPublicName(string first, string last)
    {
        var initial = string.IsNullOrWhiteSpace(last) ? "" : $" {char.ToUpperInvariant(last.Trim()[0])}.";
        return $"{first.Trim()}{initial}";
    }

    private static Subscription? PickPublicSubscription(IReadOnlyList<Subscription> subs, DateOnly today)
    {
        var ranked = subs
            .Select(s => (Sub: s, Status: SubscriptionStatus.Derive(s, today)))
            .OrderBy(x => x.Status switch
            {
                DerivedSubscriptionStatus.Active => 0,
                DerivedSubscriptionStatus.Scheduled => 1,
                DerivedSubscriptionStatus.Exhausted => 2,
                DerivedSubscriptionStatus.Expired => 3,
                DerivedSubscriptionStatus.Cancelled => 4,
                _ => 5
            })
            .ThenByDescending(x => x.Sub.CreatedAtUtc)
            .ToList();

        return ranked.FirstOrDefault().Sub;
    }
}
