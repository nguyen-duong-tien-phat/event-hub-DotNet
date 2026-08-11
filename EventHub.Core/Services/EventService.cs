using EventHub.Core.Common;
using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Core.Services.Models;

namespace EventHub.Core.Services;

public class EventService(IRepository<Event> eventRepository) {
    public async Task<PagedResult<Event>> GetPagedAsync(int page, int pageSize) {
        var (items, totalCount) = await eventRepository.GetPagedAsync(page, pageSize);
        return new PagedResult<Event> {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public Task<Event?> GetByIdAsync(Guid id) => eventRepository.GetByIdAsync(id);

    public async Task<Event> CreateAsync(CreateEventRequest request) {
        var newEvent = new Event {
            Title = request.Title,
            Description = request.Description,
            StartsAt = request.StartsAt,
            Location = request.Location,
            OrganizerId = request.OrganizerId
        };

        await eventRepository.AddAsync(newEvent);
        await eventRepository.SaveChangesAsync();
        return newEvent;
    }

    public async Task<Event?> UpdateAsync(Guid id, UpdateEventRequest request) {
        var existing = await eventRepository.GetByIdAsync(id);
        if (existing == null) return null;

        if (request.Title is not null) existing.Title = request.Title;
        if (request.Description is not null) existing.Description = request.Description;
        if (request.StartsAt is not null) existing.StartsAt = request.StartsAt.Value;
        if (request.Location is not null) existing.Location = request.Location;

        eventRepository.Update(existing);
        await eventRepository.SaveChangesAsync();
        return existing;
    }
}