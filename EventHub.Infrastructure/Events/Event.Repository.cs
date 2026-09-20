using EventHub.Core.Common;
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

    public async Task<(List<Event> Items, int TotalCount)> GetEventsQuery(
        int page,
        int pageSize,
        string? search,
        DateTime? from,
        DateTime? to
        ) {
        var query = DbSet.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search)) {
            query = query.Where(e => 
                e.Title.Contains(search) || 
                e.Location.Contains(search) || 
                e.Description.Contains(search)
                );
        }

        if (from != null) {
            query = query.Where(e => e.StartsAt >= from.Value);
        }
        if (to != null) {
            query = query.Where(e => e.StartsAt <= to.Value);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(e => e.StartsAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }
}