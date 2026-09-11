using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Repositories;

public class EventRepository(AppDbContext db): Repository<Event>(db), IEventRepository {
    public async Task<(List<Event> Items, int TotalCount)> GetPagedWithOrganizerAsync(int page, int size) {
        var totalCount = await DbSet.CountAsync();
        var items = await DbSet
            .Include(e => e.Organizer)
            .Skip(page - 1)
            .Take(size)
            .Select(e => new Event
            {
                Id = e.Id,
                Title = e.Title,
                Description = e.Description,
                StartsAt = e.StartsAt,
                Location = e.Location,
                OrganizerId = e.OrganizerId,
                ImageUrl = e.ImageUrl,
                Organizer = e.Organizer == null
                    ? null
                    : new Organizer {
                        Id = e.Organizer.Id,
                        Email = e.Organizer.Email,
                        FullName = e.Organizer.FullName
                    }
            })
            .ToListAsync();
        return (items, totalCount);
    }
}