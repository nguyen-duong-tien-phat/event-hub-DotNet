using EventHub.Core.Events;
using EventHub.Infrastructure.Common;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Events;

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