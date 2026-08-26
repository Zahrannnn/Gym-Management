namespace Gym_Management.Domain;

public class Plan
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public PlanType Type { get; set; }

    /// <summary>Required for Time plans; null for Session plans.</summary>
    public int? DurationDays { get; set; }

    /// <summary>
    /// Unused for Session plans (session count is chosen per subscription via TotalSessions).
    /// Always null; kept for schema compatibility.
    /// </summary>
    public int? Sessions { get; set; }

    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;
}
