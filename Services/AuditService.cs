using Gym_Management.Data;
using Gym_Management.Domain;

namespace Gym_Management.Services;

public interface IAuditService
{
    Task WriteAsync(Guid staffId, string action, string entityType, string entityId, string? details = null, CancellationToken cancellationToken = default);
}

public class AuditService(GymDbContext db, IGymClock clock) : IAuditService
{
    public async Task WriteAsync(
        Guid staffId,
        string action,
        string entityType,
        string entityId,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            AtUtc = clock.UtcNow,
            StaffId = staffId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
