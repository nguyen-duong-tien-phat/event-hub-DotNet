using EventHub.Core.Bookings;
using EventHub.Infrastructure.Common;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Bookings;

public class BookingRepository(AppDbContext db) : Repository<Booking>(db), IBookingRepository {
    public async Task<List<Booking>> GetByUserIdAsync(string userId) =>
        await DbSet.Where(b => b.UserId == userId).ToListAsync();

    public async Task<(List<Booking> Items, int TotalCount)> GetPagedByUserIdAsync(string userId, int page, int pageSize) {
        var query = DbSet.Where(b => b.UserId == userId);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Id) // without an order, Postgres may return rows in a different order per page
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<bool> AnyForEventAsync(string eventId) =>
        await DbSet.AnyAsync(b => b.EventId == eventId);

    // shortcut: the confirmed bookings' ticket lists are summed in memory (one jsonb column per booking);
    // fine up to tens of thousands of bookings per event, past that move the sum into SQL.
    public async Task<BookingStats> GetStatsForEventAsync(string eventId) {
        var forEvent = DbSet.AsNoTracking().Where(b => b.EventId == eventId);

        var byStatus = await forEvent
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(b => b.TotalPrice), Fees = g.Sum(b => b.ServiceFee) })
            .ToListAsync();
        var confirmedTickets = await forEvent
            .Where(b => b.Status == BookingStatus.Confirmed)
            .Select(b => b.Tickets)
            .ToListAsync();
        var checkedIn = await db.TicketCheckIns.CountAsync(c => forEvent.Any(b => b.Id == c.BookingId));

        int CountOf(BookingStatus status) => byStatus.FirstOrDefault(x => x.Status == status)?.Count ?? 0;
        var confirmed = byStatus.FirstOrDefault(x => x.Status == BookingStatus.Confirmed);
        return new BookingStats {
            Pending = CountOf(BookingStatus.Pending),
            Confirmed = CountOf(BookingStatus.Confirmed),
            Cancelled = CountOf(BookingStatus.Cancelled),
            ConfirmedTotal = confirmed?.Total ?? 0,
            ConfirmedServiceFees = confirmed?.Fees ?? 0,
            SoldByTicketId = confirmedTickets.SelectMany(t => t)
                .GroupBy(t => t.TicketId)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Quantity)),
            CheckedIn = checkedIn,
        };
    }

    public async Task<List<Booking>> GetByEventIdAsync(string eventId) =>
        await DbSet
            .Where(b => b.EventId == eventId)
            .ToListAsync();
    
    public async Task<(List<Booking> Items, int TotalCount)> GetPagedByEventIdAsync(string eventId, int page, int pageSize) {
        var query = DbSet.Where(b => b.EventId == eventId);

        var totalCount = await query.CountAsync();
        var items = await query
            .Include(b => b.User) // the organizer's list shows who bought; one JOIN, not a query per booking
            .OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public void DetachPending() {
        foreach (var entry in db.ChangeTracker.Entries().ToList()) {
            entry.State = EntityState.Detached;
        }
    }

    public async Task<Booking?> GetByIdempotencyKeyAsync(string userId, string key) =>
        await DbSet.FirstOrDefaultAsync(b => b.UserId == userId && b.IdempotencyKey == key);

    public async Task<Booking?> GetByPaymentIntentId(string paymentIntentId) =>
        await DbSet.FirstOrDefaultAsync(b => b.PaymentIntentId == paymentIntentId);

    public async Task<List<Booking>> GetExpiredPendingBookingsAsync(DateTime olderThan) =>
        await DbSet
            .Where(b => b.Status == BookingStatus.Pending && b.CreatedAt < olderThan)
            .ToListAsync();

}