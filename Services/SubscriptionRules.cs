using Gym_Management.Domain;

namespace Gym_Management.Services;

/// <summary>Overlap / renewal rules (AGENTS rules 1 and 3).</summary>
public static class SubscriptionRules
{
    /// <summary>
    /// Returns null when creation is allowed; otherwise an overlap conflict detail message.
    /// </summary>
    public static string? ValidateNewSubscription(
        IReadOnlyList<Subscription> existingForCustomer,
        DateOnly newStartDate,
        DateOnly today)
    {
        var nonTerminal = existingForCustomer
            .Where(s => SubscriptionStatus.IsNonTerminal(s, today))
            .ToList();

        if (nonTerminal.Count == 0)
        {
            return null;
        }

        // Live session-based sub blocks everything until staff cancels it.
        if (nonTerminal.Any(s => s.Type == SubscriptionType.Session))
        {
            return "A non-terminal session subscription already exists for this customer.";
        }

        // Time-based: allow exactly one future renewal starting on/after day after current end.
        if (nonTerminal.Count > 1)
        {
            return "A non-terminal subscription already exists for this customer.";
        }

        var current = nonTerminal[0];
        if (current.Type != SubscriptionType.Time || current.EndDate is null)
        {
            return "A non-terminal subscription already exists for this customer.";
        }

        var earliestRenewalStart = current.EndDate.Value.AddDays(1);
        if (newStartDate >= earliestRenewalStart)
        {
            return null;
        }

        return $"Renewal start date must be on or after {earliestRenewalStart:yyyy-MM-dd} (day after the current time-based subscription ends).";
    }
}
