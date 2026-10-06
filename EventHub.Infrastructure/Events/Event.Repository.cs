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
            .Include(e => e.Organizer)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<List<Event>> GetFeaturedEventsAsync(int limit) {
        var items = await DbSet
            .AsNoTracking()
            .Where(e => e.StartsAt > DateTime.UtcNow)
            .Where(e => e.Tickets.Any())
            .OrderBy(e => (double)e.Tickets.Sum(t => t.RemainingQuantity) / e.Tickets.Sum(t => t.TotalQuantity))
            .ThenByDescending(e => e.Tickets.Sum(t => t.TotalQuantity - t.RemainingQuantity))
            .Take(limit)
            .Include(e => e.Organizer)
            .ToListAsync();
        return items;
    }
    
    public async Task<List<Event>> GetUpcomingEventsAsync(int limit) {
        var items = await DbSet
            .AsNoTracking()
            .Where(e => e.StartsAt > DateTime.UtcNow)
            .OrderBy(e => e.StartsAt)
            .Take(limit)
            .Include(e => e.Organizer)
            .ToListAsync();
        return items;
    }
}