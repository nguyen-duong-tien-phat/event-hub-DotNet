using System.Text.Json;
using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Files;
using EventHub.Core.Tickets;
using EventHub.Core.Users;

namespace EventHub.Core.Events;

public class EventHasBookingsException() : Exception("This event already has bookings and cannot be deleted.");

public class EventService(
    IEventRepository eventRepository,
    ITicketRepository ticketRepository,
    IBookingRepository bookingRepository,
    ICacheService cache,
    UploadService uploads)
{
    private readonly string _cacheKeyPrefix = "events:";
    
    public async Task<PagedResult<Event>> GetPagedAsync(
        int page, 
        int pageSize, 
        string? search, 
        string? category,
        decimal? minPrice,
        decimal? maxPrice,
        DateTime? from,
        DateTime? to,
        string? organizerId = null)
    {
        var searchNormalize = search != null ? search.Trim().ToLower() :  string.Empty;
        var categoryNormalize = category != null ? category.Trim().ToLower() : string.Empty;
        var cacheKey = $"{_cacheKeyPrefix}page={page}:pageSize={pageSize}:search={searchNormalize}:category={categoryNormalize}:minPrice={minPrice}:maxPrice={maxPrice}:from={from}:to={to}:organizer={organizerId}";
        var cached = await cache.GetAsync(cacheKey);
        if (cached != null) {
            return JsonSerializer.Deserialize<PagedResult<Event>>(cached)!;
        }

        var (items, totalCount) = await eventRepository.GetEventsQuery(page, pageSize, search, category, minPrice, maxPrice, from, to, organizerId);
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
        var eventResponse = new EventResponse {
            Id = eventDetail.Id,
            Title = eventDetail.Title,
            Description = eventDetail.Description,
            StartsAt = eventDetail.StartsAt,
            Location = eventDetail.Location,
            ImageUrl = eventDetail.ImageUrl,
            Highlights = eventDetail.Highlights,
            Category = eventDetail.Category,
            EndsAt = eventDetail.EndsAt,
            DoorsOpenAt = eventDetail.DoorsOpenAt,
            AgeRestriction = eventDetail.AgeRestriction,
            RefundPolicy = eventDetail.RefundPolicy,
            Tickets = tickets,
            Organizer = new Organizer {
                Id = eventDetail.Organizer!.Id,
                FullName = eventDetail.Organizer.FullName,
                Email = eventDetail.Organizer.Email,
            }
        };
        
        return eventResponse;
    }
    
    public async Task<List<Event>> GetFeaturedEventsAsync(int limit) =>
        await eventRepository.GetFeaturedEventsAsync(limit);
    
    public async Task<List<string>> GetCategoriesAsync() =>
        await eventRepository.GetUpcomingCategoriesAsync();

    public async Task<List<Event>> GetUpcomingEventsAsync(int limit) =>
        await eventRepository.GetUpcomingEventsAsync(limit);


    public async Task<Event> CreateAsync(CreateEventRequest request) {
        var newEvent = new Event {
            Title = request.Title,
            Description = request.Description,
            StartsAt = request.StartsAt,
            Location = request.Location,
            OrganizerId = request.OrganizerId,
            ImageUrl = request.ImageUrl,
            Highlights = request.Highlights,
            Category = NormalizeCategory(request.Category),
            EndsAt = request.EndsAt,
            DoorsOpenAt = request.DoorsOpenAt,
            AgeRestriction = request.AgeRestriction,
            RefundPolicy = request.RefundPolicy
        };
        ValidateTimes(newEvent);
        ValidateImageUrl(newEvent.ImageUrl);

        await eventRepository.AddAsync(newEvent);
        await eventRepository.SaveChangesAsync();

        await cache.RemoveByPrefixAsync(_cacheKeyPrefix);
        
        return newEvent;
    }

    public async Task<Event?> UpdateAsync(
        string requestId, 
        UserRole requestRole, 
        string eventId, 
        UpdateEventRequest request) 
    {
        var existing = await eventRepository.GetByIdAsync(eventId);
        if (existing == null) return null;
        
        EventAccess.EnsureCanManage(requestId, requestRole, existing);

        if (request.Title is not null) existing.Title = request.Title;
        if (request.Description is not null) existing.Description = request.Description;
        if (request.StartsAt is not null) existing.StartsAt = request.StartsAt.Value;
        if (request.Location is not null) existing.Location = request.Location;
        if (request.Category is not null) existing.Category = NormalizeCategory(request.Category);
        if (request.EndsAt is not null) existing.EndsAt = request.EndsAt;
        if (request.DoorsOpenAt is not null) existing.DoorsOpenAt = request.DoorsOpenAt;
        if (request.AgeRestriction is not null) existing.AgeRestriction = request.AgeRestriction;
        if (request.RefundPolicy is not null) existing.RefundPolicy = request.RefundPolicy;
        if (request.ImageUrl is not null) {
            ValidateImageUrl(request.ImageUrl);
            existing.ImageUrl = request.ImageUrl;
        }
        ValidateTimes(existing);

        eventRepository.Update(existing);
        await eventRepository.SaveChangesAsync();
        
        await cache.RemoveByPrefixAsync(_cacheKeyPrefix);
        
        return existing;
    }

    /// <summary>
    /// Deletes an event and its poster. Returns false when the event does not exist.
    /// Checks the caller first, then refuses if anyone has booked: <c>Booking.EventId</c> has no foreign key,
    /// so deleting would silently leave paid bookings pointing at nothing.
    /// shortcut: a booking made between the check and the delete is not caught; wrap in a transaction if that matters.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The caller is neither the organizer nor an admin.</exception>
    /// <exception cref="EventHasBookingsException">The event has bookings.</exception>
    public async Task<bool> DeleteAsync(string requestId, UserRole requestRole, string eventId) {
        var existing = await eventRepository.GetByIdAsync(eventId);
        if (existing == null) return false;

        EventAccess.EnsureCanManage(requestId, requestRole, existing);
        if (await bookingRepository.AnyForEventAsync(eventId)) throw new EventHasBookingsException();

        eventRepository.Delete(existing);
        await eventRepository.SaveChangesAsync();
        await cache.RemoveByPrefixAsync(_cacheKeyPrefix);

        // After the commit: a failed file delete only orphans the poster, it must not undo the delete.
        await uploads.DeleteUploadedAsync(UploadKind.EventImage, existing.ImageUrl);
        return true;
    }

    private static string NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? "Other" : category.Trim();

    /// <summary>Only images uploaded through <see cref="UploadService"/> may be attached, not arbitrary URLs.</summary>
    private void ValidateImageUrl(string imageUrl) {
        if (!uploads.IsUploadedUrl(UploadKind.EventImage, imageUrl)) {
            throw new ArgumentException("ImageUrl must be the fileUrl returned by POST /events/image-upload.");
        }
    }

    /// <summary>Doors open before the event starts, and it ends after it starts.</summary>
    private static void ValidateTimes(Event ev) {
        if (ev.EndsAt is not null && ev.EndsAt <= ev.StartsAt) {
            throw new ArgumentException("EndsAt must be later than StartsAt.");
        }
        if (ev.DoorsOpenAt is not null && ev.DoorsOpenAt > ev.StartsAt) {
            throw new ArgumentException("DoorsOpenAt must not be later than StartsAt.");
        }
    }
}
