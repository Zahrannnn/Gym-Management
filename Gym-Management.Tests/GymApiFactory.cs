using Gym_Management.Data;
using Gym_Management.Domain;
using Gym_Management.Tests.TestApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gym_Management.Tests;

public class GymApiFactory : WebApplicationFactory<Program>
{
    public string DatabaseName { get; } = $"GymTests_{Guid.NewGuid():N}";

    public string TestAdminUsername => "admin";
    public string TestAdminPassword => "Admin#12345!";
    public string TestStaffUsername => "staff1";
    public string TestStaffPassword => "Staff#12345!";

    /// <summary>
    /// Creates the empty test database, boots the in-process API host (whose startup runs
    /// Database.Migrate + settings/admin seeds against it), then seeds a Staff user to
    /// prove role separation.
    /// </summary>
    public async Task StartAsync()
    {
        await LocalDb.CreateDatabaseAsync(DatabaseName);
        CreateClient(); // forces host build; Program applies migrations and seeds
        await SeedStaffUserAsync();
    }

    public async Task ShutdownAsync()
    {
        await DisposeAsync();
        await LocalDb.DropDatabaseAsync(DatabaseName);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = LocalDb.ConnectionStringFor(DatabaseName),
                ["Jwt:Key"] = "test-only-jwt-signing-key-32-chars-min!!",
                ["Jwt:Issuer"] = "GymTests",
                ["Jwt:Audience"] = "GymTests",
                ["AdminSeed:Username"] = TestAdminUsername,
                ["AdminSeed:Password"] = TestAdminPassword,
                ["AdminSeed:FullName"] = "Test Administrator",
                ["Cors:PortalOrigin"] = "http://localhost:3000"
            });
        });

        // Register the test-only admin probe controller (role-separation proof, task brief).
        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(TestAdminProbeController).Assembly);
        });
    }

    private async Task SeedStaffUserAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<StaffUser>>();

        var staff = new StaffUser
        {
            Id = Guid.NewGuid(),
            Username = TestStaffUsername,
            FullName = "Test Staff",
            Role = StaffRole.Staff,
            IsActive = true
        };
        staff.PasswordHash = hasher.HashPassword(staff, TestStaffPassword);

        db.StaffUsers.Add(staff);
        await db.SaveChangesAsync();
    }
}
