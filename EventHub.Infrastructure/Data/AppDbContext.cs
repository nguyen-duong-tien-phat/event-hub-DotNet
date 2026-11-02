using EventHub.Core.Auth;
using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.SavedEvents;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options): DbContext(options) {
    public DbSet<User> Users => Set<User>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<SavedEvent> SavedEvents => Set<SavedEvent>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();
    public DbSet<TicketCheckIn> TicketCheckIns => Set<TicketCheckIn>();
    
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
        
        modelBuilder.Entity<Booking>()
            .Property(x => x.Tickets)
            .HasColumnType("jsonb");

        // One booking per (user, key). Old bookings have no key, so only rows with one are constrained.
        modelBuilder.Entity<Booking>()
            .HasIndex(b => new { b.UserId, b.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        
        modelBuilder.Entity<RefreshToken>().Property(t => t.Id).ValueGeneratedNever();
        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.TokenHash).IsUnique();
        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.UserId);

        modelBuilder.Entity<PasswordResetCode>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<PasswordResetCode>().HasIndex(c => c.UserId);

        // A ticket gets in once: this pair is unique.
        modelBuilder.Entity<TicketCheckIn>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<TicketCheckIn>().HasIndex(c => new { c.BookingId, c.Number }).IsUnique();

        modelBuilder.Entity<SavedEvent>().Property(s => s.Id).ValueGeneratedNever();
        modelBuilder.Entity<SavedEvent>()
            .HasIndex(s => new { s.UserId, s.EventId })
            .IsUnique();
        modelBuilder.Entity<SavedEvent>()
            .HasOne(s => s.Event)
            .WithMany()
            .HasForeignKey(s => s.EventId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SavedEvent>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(modelBuilder);
    }
}