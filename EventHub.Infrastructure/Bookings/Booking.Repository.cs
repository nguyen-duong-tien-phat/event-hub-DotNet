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
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<List<Booking>> GetByEventIdAsync(string eventId) =>
        await DbSet
            .Where(b => b.EventId == eventId)
            .ToListAsync();
    
    public async Task<(List<Booking> Items, int TotalCount)> GetPagedByEventIdAsync(string eventId, int page, int pageSize) {
        var query = DbSet.Where(b => b.EventId == eventId);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Booking?> GetByPaymentIntentId(string paymentIntentId) =>
        await DbSet.FirstOrDefaultAsync(b => b.PaymentIntentId == paymentIntentId);

    public async Task<List<Booking>> GetExpiredPendingBookingsAsync(DateTime olderThan) =>
        await DbSet
            .Where(b => b.Status == BookingStatus.Pending && b.CreatedAt < olderThan)
            .ToListAsync();

}