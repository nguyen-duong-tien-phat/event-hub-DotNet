using System.Text.Json;
using EventHub.Core.Common;
using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Core.Services.Models;

namespace EventHub.Core.Services;

public class EventsService(
    IEventRepository eventRepository, 
    ITicketRepository ticketRepository, 
    ICacheService cache) 
{
    private readonly string _cacheKeyPrefix = "events:";
    
    public async Task<PagedResult<Event>> GetPagedAsync(int page, int pageSize) {
        var cacheKey = $"{_cacheKeyPrefix}page={page}:pageSize={pageSize}";
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

    public async Task<EventResponse?> GetByIdAsync(string id) {
        var eventDetail = await eventRepository.GetEventDetailByIdAsync(id);
        if (eventDetail  == null) return null;
        var tickets = await ticketRepository.GetByEventIdAsync(id);
        var eventReponse = new EventResponse {
            Id = eventDetail.Id,
            Title = eventDetail.Title,
            Description = eventDetail.Description,
            StartsAt = eventDetail.StartsAt,
            Location = eventDetail.Location,
            ImageUrl = eventDetail.ImageUrl,
            Highlights = eventDetail.Highlights,
            Tickets = tickets,
            Organizer = new Organizer {
                Id = eventDetail.Organizer.Id,
                Email = eventDetail.Organizer.Email,
                FullName = eventDetail.Organizer.FullName,
                Role = eventDetail.Organizer.Role.ToString(),
            }
        };
        
        return eventReponse;
    }


    public async Task<Event> CreateAsync(CreateEventRequest request) {
        var newEvent = new Event {
            Title = request.Title,
            Description = request.Description,
            StartsAt = request.StartsAt,
            Location = request.Location,
            OrganizerId = request.OrganizerId,
            ImageUrl = request.ImageUrl,
            Highlights = request.Highlights
        };

        await eventRepository.AddAsync(newEvent);
        await eventRepository.SaveChangesAsync();

        await cache.RemoveByPrefixAsync(_cacheKeyPrefix);
        
        return newEvent;
    }

    public async Task<Event?> UpdateAsync(string id, UpdateEventRequest request) {
        var existing = await eventRepository.GetByIdAsync(id);
        if (existing == null) return null;

        if (request.Title is not null) existing.Title = request.Title;
        if (request.Description is not null) existing.Description = request.Description;
        if (request.StartsAt is not null) existing.StartsAt = request.StartsAt.Value;
        if (request.Location is not null) existing.Location = request.Location;

        eventRepository.Update(existing);
        await eventRepository.SaveChangesAsync();
        
        await cache.RemoveByPrefixAsync(_cacheKeyPrefix);
        
        return existing;
    }
}