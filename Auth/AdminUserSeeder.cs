using Gym_Management.Data;
using Gym_Management.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Gym_Management.Auth;

/// <summary>
/// Idempotent startup seed of the admin account from the AdminSeed configuration
/// section. Default credentials: admin / Admin#12345! — documented in README.md,
/// must be overridden via configuration in any real deployment.
/// </summary>
public class AdminUserSeeder(GymDbContext db, IPasswordHasher<StaffUser> passwordHasher, IConfiguration configuration)
{
    public const string DefaultUsername = "admin";
    public const string DefaultPassword = "Admin#12345!";
    public const string DefaultFullName = "System Administrator";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var username = configuration["AdminSeed:Username"];
        if (string.IsNullOrWhiteSpace(username))
        {
            username = DefaultUsername;
        }

        var exists = await db.StaffUsers.AnyAsync(u => u.Username == username, cancellationToken);
        if (exists)
        {
            return;
        }

        var password = configuration["AdminSeed:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            password = DefaultPassword;
        }

        var fullName = configuration["AdminSeed:FullName"];
        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = DefaultFullName;
        }

        var admin = new StaffUser
        {
            Id = Guid.NewGuid(),
            Username = username,
            FullName = fullName,
            Role = StaffRole.Admin,
            IsActive = true
        };
        admin.PasswordHash = passwordHasher.HashPassword(admin, password);

        db.StaffUsers.Add(admin);
        await db.SaveChangesAsync(cancellationToken);
    }
}
