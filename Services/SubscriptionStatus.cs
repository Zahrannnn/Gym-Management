using Gym_Management.Domain;

namespace Gym_Management.Services;

/// <summary>Derived at read time — never stored (rule 4).</summary>
public enum DerivedSubscriptionStatus
{
    Cancelled,
    Scheduled,
    Active,
    Expired,
    Exhausted
}

public static class SubscriptionStatus
{
    public static DerivedSubscriptionStatus Derive(Subscription sub, DateOnly today)
    {
        if (sub.CancelledAtUtc is not null)
        {
            return DerivedSubscriptionStatus.Cancelled;
        }

        if (sub.StartDate > today)
        {
            return DerivedSubscriptionStatus.Scheduled;
        }

        if (sub.Type == SubscriptionType.Time)
        {
            if (sub.EndDate is null || today > sub.EndDate.Value)
            {
                return DerivedSubscriptionStatus.Expired;
            }

            return DerivedSubscriptionStatus.Active;
        }

        // Session-based: no end date / validity window (rule 2).
        var total = sub.TotalSessions ?? 0;
        if (sub.UsedSessions >= total)
        {
            return DerivedSubscriptionStatus.Exhausted;
        }

        return DerivedSubscriptionStatus.Active;
    }

    /// <summary>Non-terminal = not cancelled, not expired (time), not exhausted (session).</summary>
    public static bool IsNonTerminal(Subscription sub, DateOnly today)
    {
        var status = Derive(sub, today);
        return status is DerivedSubscriptionStatus.Active or DerivedSubscriptionStatus.Scheduled;
    }

    public static string ToApiString(DerivedSubscriptionStatus status) => status switch
    {
        DerivedSubscriptionStatus.Cancelled => "Cancelled",
        DerivedSubscriptionStatus.Scheduled => "Scheduled",
        DerivedSubscriptionStatus.Active => "Active",
        DerivedSubscriptionStatus.Expired => "Expired",
        DerivedSubscriptionStatus.Exhausted => "Exhausted",
        _ => status.ToString()
    };
}
