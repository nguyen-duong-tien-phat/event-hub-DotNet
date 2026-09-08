using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Repositories;

public class TicketRepository(AppDbContext db) : Repository<Ticket>(db), ITicketRepository {
    public async Task<List<Ticket>> GetByEventIdAsync(string eventId) =>
        await Db.Tickets.Where(t => t.EventId == eventId).ToListAsync();
    
    public async Task<(List<Ticket> Items, int TotalCount)> GetPagedByEventIdAsync(string eventId, int page, int pageSize) {
        var query = Db.Tickets.Where(t => t.EventId == eventId);
        
        var totalCount = await query.CountAsync();
        var items =  await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, totalCount);
    }
    
    public async Task<bool> TryReserveAsync(string ticketId, int quantity) {
        var rowsAffected = await Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Tickets\" SET \"RemainingQuantity\" = \"RemainingQuantity\" - {quantity} WHERE \"Id\" = {ticketId} AND \"RemainingQuantity\" >= {quantity}"
        );
        return rowsAffected > 0;
    }
    
    public async Task ReleaseAsync(string ticketId, int quantity) {
        await Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Tickets\" SET \"RemainingQuantity\" = \"RemainingQuantity\" + {quantity} WHERE \"Id\" = {ticketId}"
        );
    }
}