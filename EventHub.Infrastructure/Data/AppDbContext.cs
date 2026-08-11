using EventHub.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Data;

public class AppDbContext: DbContext {
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Booking> Bookings => Set<Booking>();
    
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) {
        var entries = ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries) {
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}