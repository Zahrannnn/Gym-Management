using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gym_Management.Data;

/// <summary>
/// Design-time factory so `dotnet ef` can create the context without running Program.cs
/// (Plesk/IIS production has no shell; migrations are applied at startup via Database.Migrate()).
/// </summary>
public class GymDbContextFactory : IDesignTimeDbContextFactory<GymDbContext>
{
    public GymDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseSqlServer(
                @"Server=(localdb)\MSSQLLocalDB;Database=Gym-Management;Trusted_Connection=True;MultipleActiveResultSets=true")
            .Options;

        return new GymDbContext(options);
    }
}
