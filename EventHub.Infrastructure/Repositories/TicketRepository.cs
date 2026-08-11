using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Repositories;

public class TicketRepository(AppDbContext db) : Repository<Ticket>(db), ITicketRepository {
    public async Task<List<Ticket>> GetByEventIdAsync(Guid eventId) =>
        await Db.Tickets.Where(t => t.EventId == eventId).ToListAsync();
}