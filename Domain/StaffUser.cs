namespace Gym_Management.Domain;

public class StaffUser
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public StaffRole Role { get; set; } = StaffRole.Staff;

    public bool IsActive { get; set; } = true;
}
