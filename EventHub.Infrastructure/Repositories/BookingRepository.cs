using EventHub.Core.Common;
using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Repositories;

public class BookingRepository(AppDbContext db) : Repository<Booking>(db), IBookingRepository {
    public async Task<List<Booking>> GetByUserIdAsync(Guid userId) =>
        await Db.Bookings.Where(b => b.UserId == userId).ToListAsync();

    public async Task<(List<Booking> Items, int TotalCount)> GetPagedByUserIdAsync(Guid userId, int page, int pageSize) {
        var query = Db.Bookings.Where(b => b.UserId == userId);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<List<Booking>> GetByEventIdAsync(Guid eventId) =>
        await Db.Bookings
            .Where(b => b.Ticket!.EventId == eventId)
            .Include(b => b.Ticket)
            .ToListAsync();
    
    public async Task<(List<Booking> Items, int TotalCount)> GetPagedByEventIdAsync(Guid eventId, int page, int pageSize) {
        var query = Db.Bookings.Where(b => b.Ticket!.EventId == eventId);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Booking?> GetByPaymentIntentId(string paymentIntentId) =>
        await Db.Bookings.FirstOrDefaultAsync(b => b.PaymentIntentId == paymentIntentId);
}