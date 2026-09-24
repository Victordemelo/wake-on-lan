using Microsoft.EntityFrameworkCore;
using RemoteWake.Api.Models;

namespace RemoteWake.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<WakeAttempt> WakeAttempts => Set<WakeAttempt>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasIndex(user => user.Email)
            .IsUnique();

        modelBuilder.Entity<Machine>()
            .HasOne(machine => machine.Owner)
            .WithMany(user => user.Machines)
            .HasForeignKey(machine => machine.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WakeAttempt>(attempt =>
        {
            attempt.HasOne(item => item.Machine)
                .WithMany(machine => machine.WakeAttempts)
                .HasForeignKey(item => item.MachineId)
                .OnDelete(DeleteBehavior.SetNull);
            attempt.HasOne<User>()
                .WithMany()
                .HasForeignKey(item => item.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
            attempt.HasIndex(item => new { item.OwnerId, item.RequestedAt });
            attempt.Property(item => item.IpAddress).HasMaxLength(45);
        });

        modelBuilder.Entity<UserSession>(session =>
        {
            session.HasOne(item => item.User)
                .WithMany(user => user.Sessions)
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            session.HasIndex(item => item.TokenHash).IsUnique();
            session.Property(item => item.TokenHash).HasMaxLength(64);
            session.Property(item => item.IpAddress).HasMaxLength(45);
            session.Property(item => item.UserAgent).HasMaxLength(256);
        });

        modelBuilder.Entity<RecoveryCode>(code =>
        {
            code.HasOne(item => item.User)
                .WithMany(user => user.RecoveryCodes)
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            code.Property(item => item.CodeHash).HasMaxLength(64);
        });

        modelBuilder.Entity<SecurityEvent>(securityEvent =>
        {
            securityEvent.HasOne(item => item.User)
                .WithMany()
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            securityEvent.HasIndex(item => new { item.UserId, item.CreatedAt });
            securityEvent.Property(item => item.Type).HasMaxLength(40);
            securityEvent.Property(item => item.IpAddress).HasMaxLength(45);
            securityEvent.Property(item => item.UserAgent).HasMaxLength(256);
        });
    }
}
