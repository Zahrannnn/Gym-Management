using Gym_Management.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gym_Management.Data;

public class GymDbContext(DbContextOptions<GymDbContext> options) : DbContext(options)
{
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StaffUser>(entity =>
        {
            entity.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            entity.Property(u => u.Username).HasMaxLength(100).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            entity.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.Property(c => c.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(c => c.LastName).HasMaxLength(100).IsRequired();
            entity.Property(c => c.Phone).HasMaxLength(30).IsRequired();
            entity.Property(c => c.Token).HasMaxLength(64).IsRequired();
            entity.Property(c => c.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(c => c.Notes).HasMaxLength(2000);
            entity.HasIndex(c => c.TokenHash).IsUnique();
            entity.HasIndex(c => c.Phone);
            entity.HasOne<StaffUser>()
                .WithMany()
                .HasForeignKey(c => c.CreatedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Plan>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Price).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.Property(s => s.EndDate).HasColumnType("date");
            entity.Property(s => s.StartDate).HasColumnType("date");
            entity.Property(s => s.PricePaid).HasColumnType("decimal(18,2)");
            entity.Property(s => s.UsedSessions).HasDefaultValue(0);
            entity.Property(s => s.CancelReason).HasMaxLength(500);
            entity.HasIndex(s => s.CustomerId);
            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(s => s.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Plan>()
                .WithMany()
                .HasForeignKey(s => s.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StaffUser>()
                .WithMany()
                .HasForeignKey(s => s.CreatedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AttendanceLog>(entity =>
        {
            entity.Property(a => a.Reason).HasMaxLength(100);
            // Most recent attendance first for per-customer lookups (rule 9 duplicate scan check).
            entity.HasIndex(a => new { a.CustomerId, a.AtUtc }).IsDescending(false, true);
            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(a => a.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Subscription>()
                .WithMany()
                .HasForeignKey(a => a.SubscriptionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<StaffUser>()
                .WithMany()
                .HasForeignKey(a => a.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            entity.Property(p => p.Note).HasMaxLength(500);
            entity.HasIndex(p => p.SubscriptionId);
            entity.HasOne<Subscription>()
                .WithMany()
                .HasForeignKey(p => p.SubscriptionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(p => p.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StaffUser>()
                .WithMany()
                .HasForeignKey(p => p.RecordedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Setting>(entity =>
        {
            entity.Property(s => s.Key).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Value).HasMaxLength(500).IsRequired();
            entity.HasIndex(s => s.Key).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(a => a.Action).HasMaxLength(100).IsRequired();
            entity.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Details).HasMaxLength(2000);
            entity.HasIndex(a => a.EntityType);
            entity.HasOne<StaffUser>()
                .WithMany()
                .HasForeignKey(a => a.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
