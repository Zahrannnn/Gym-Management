namespace Gym_Management.Domain;

public class Customer
{
    public Guid Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    /// <summary>Raw QR token (Base64Url). Returned on staff customer APIs; rotated only via token reset.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the QR token (Base64Url) for secure lookup.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime? TokenRotatedAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid? CreatedByStaffId { get; set; }
}
