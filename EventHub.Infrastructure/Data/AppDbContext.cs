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
    
    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<User>().Property(u => u.Id).ValueGeneratedNever();
        modelBuilder.Entity<Event>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<Ticket>().Property(t => t.Id).ValueGeneratedNever();
        modelBuilder.Entity<Booking>().Property(b => b.Id).ValueGeneratedNever();
        
        modelBuilder.Entity<User>()
            .Property(u => u.Role)
            .HasConversion<string>();
        
        modelBuilder.Entity<Ticket>()
            .Property(x => x.Highlights)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]");
        
        modelBuilder.Entity<Event>()
            .Property(e => e.Highlights)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]");

        modelBuilder.Entity<Booking>()
            .Property(b => b.Status)
            .HasConversion<string>();
        
        base.OnModelCreating(modelBuilder);
    }
}