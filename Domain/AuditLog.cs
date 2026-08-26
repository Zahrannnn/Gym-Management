namespace Gym_Management.Domain;

public class AuditLog
{
    public Guid Id { get; set; }

    public DateTime AtUtc { get; set; }

    public Guid StaffId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string? Details { get; set; }
}
