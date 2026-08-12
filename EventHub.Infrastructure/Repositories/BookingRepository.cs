using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Repositories;

public class BookingRepository(AppDbContext db) : Repository<Booking>(db), IBookingRepository {
    public async Task<List<Booking>> GetByUserIdAsync(Guid userId) =>
        await Db.Bookings.Where(b => b.UserId == userId).ToListAsync();
    
    public async Task<List<Booking>> GetByEventIdAsync(Guid eventId) =>
        await Db.Bookings
            .Where(b => b.Ticket!.EventId == eventId)
            .Include(b => b.Ticket)
            .ToListAsync();
}