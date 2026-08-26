namespace Gym_Management.Domain;

public class Subscription
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public Guid PlanId { get; set; }

    /// <summary>Snapshot of the plan type at purchase time.</summary>
    public SubscriptionType Type { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>Time plans only (inclusive). Session plans have no end date and no validity window.</summary>
    public DateOnly? EndDate { get; set; }

    public int? TotalSessions { get; set; }

    public int UsedSessions { get; set; }

    public decimal? PricePaid { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancelReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedByStaffId { get; set; }
}
