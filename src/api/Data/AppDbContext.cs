using Microsoft.EntityFrameworkCore;
using RemoteWake.Api.Models;

namespace RemoteWake.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<WakeAttempt> WakeAttempts => Set<WakeAttempt>();

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

        modelBuilder.Entity<WakeAttempt>()
            .HasOne(attempt => attempt.Machine)
            .WithMany(machine => machine.WakeAttempts)
            .HasForeignKey(attempt => attempt.MachineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

