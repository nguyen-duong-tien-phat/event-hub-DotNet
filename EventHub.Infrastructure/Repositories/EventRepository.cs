using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Repositories;

public class EventRepository(AppDbContext db): Repository<Event>(db), IEventRepository {
    public async Task<Event?> GetEventDetailByIdAsync(string id) {
        var eventEntity = await DbSet
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Include(e => e.Organizer)
            .FirstOrDefaultAsync();
        return eventEntity;
    }
}