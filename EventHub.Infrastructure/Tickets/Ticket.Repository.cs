using EventHub.Core.Bookings;
using EventHub.Core.Tickets;
using EventHub.Infrastructure.Common;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Tickets;

public class TicketRepository(AppDbContext db) : Repository<Ticket>(db), ITicketRepository {
    public async Task<List<Ticket>> GetByEventIdAsync(string eventId) =>
        await Db.Tickets.Where(t => t.EventId == eventId).ToListAsync();
    
    public async Task<(List<Ticket> Items, int TotalCount)> GetPagedByEventIdAsync(string eventId, int page, int pageSize) {
        var query = Db.Tickets.Where(t => t.EventId == eventId);
        
        var totalCount = await query.CountAsync();
        var items =  await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, totalCount);
    }
    
    public async Task<bool> TryReserveAsync(List<BookingTicket> items) {
        foreach (var item in items) {
            var rowsAffected = await Db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Tickets\" SET \"RemainingQuantity\" = \"RemainingQuantity\" - {item.Quantity} WHERE \"Id\" = {item.TicketId} AND \"RemainingQuantity\" >= {item.Quantity}"
            );

            if (rowsAffected == 0) {
                return false;
            }
        }

        return true;
    }
    
    public async Task ReleaseAsync(List<BookingTicket> items) {
        foreach (var item in items) {
            await Db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Tickets\" SET \"RemainingQuantity\" = \"RemainingQuantity\" + {item.Quantity} WHERE \"Id\" = {item.TicketId}"
            );
        }
    }
}