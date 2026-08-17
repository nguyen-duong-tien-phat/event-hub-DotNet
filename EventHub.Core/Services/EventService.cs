using System.Text.Json;
using EventHub.Core.Common;
using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Core.Services.Models;

namespace EventHub.Core.Services;

public class EventService(IRepository<Event> eventRepository, ICacheService cache) {
    public async Task<PagedResult<Event>> GetPagedAsync(int page, int pageSize) {
        var cacheKey = $"events:page={page}:pageSize={pageSize}";
        var cached = await cache.GetAsync(cacheKey);
        if (cached != null) {
            return JsonSerializer.Deserialize<PagedResult<Event>>(cached)!;
        }

        var (items, totalCount) = await eventRepository.GetPagedAsync(page, pageSize);
        var result = new PagedResult<Event> {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
        await cache.SetAsync(cacheKey, JsonSerializer.Serialize(result), TimeSpan.FromMinutes(5));
        return result;
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