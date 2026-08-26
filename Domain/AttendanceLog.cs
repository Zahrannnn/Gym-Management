namespace Gym_Management.Domain;

/// <summary>Append-only. Every staff scan (granted AND denied) writes one row. Never updated or deleted.</summary>
public class AttendanceLog
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public Guid? SubscriptionId { get; set; }

    public Guid StaffId { get; set; }

    public DateTime AtUtc { get; set; }

    public AttendanceResult Result { get; set; }

    public string? Reason { get; set; }

    public int? RemainingSessionsAfter { get; set; }
}
