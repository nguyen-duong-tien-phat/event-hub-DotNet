using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Users;

namespace EventHub.Core.Tickets;

public class TicketHasSalesException() : Exception("Some of this ticket is already sold or held, so it cannot be deleted.");

public class TicketService(ITicketRepository ticketRepository, IEventRepository eventRepository, ICacheService cache) {
    public Task<List<Ticket>> GetByEventIdAsync(string eventId) =>
        ticketRepository.GetByEventIdAsync(eventId);

    public Task<Ticket?> GetByIdAsync(string id) => ticketRepository.GetByIdAsync(id);
    
    public async Task<PagedResult<Ticket>> GetPagedByEventIdAsync(string evenId, int page, int pageSize) {
        var (items, totalCount) = await ticketRepository.GetPagedByEventIdAsync(evenId, page, pageSize);
        return new PagedResult<Ticket> {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    /// <summary>Returns null when the event does not exist.</summary>
    /// <exception cref="UnauthorizedAccessException">The caller is neither the event's organizer nor an admin.</exception>
    public async Task<Ticket?> CreateAsync(string requestId, UserRole requestRole, CreateTicketRequest request) {
        var ev = await eventRepository.GetByIdAsync(request.EventId);
        if (ev == null) return null;
        EventAccess.EnsureCanManage(requestId, requestRole, ev);

        var ticket = new Ticket {
            EventId = request.EventId,
            Type = request.Type,
            Price = request.Price,
            TotalQuantity = request.TotalQuantity,
            Highlights = request.Highlights,
            MaxPerOrder = request.MaxPerOrder,
            RemainingQuantity = request.TotalQuantity // starts full, business rule
        };

        await ticketRepository.AddAsync(ticket);
        await ticketRepository.SaveChangesAsync();
        await cache.RemoveByPrefixAsync(EventService.CacheKeyPrefix); // a new cheaper ticket changes minPrice
        return ticket;
    }

    /// <exception cref="UnauthorizedAccessException">The caller is neither the event's organizer nor an admin.</exception>
    public async Task<Ticket?> UpdateAsync(string requestId, UserRole requestRole, string id, UpdateTicketRequest request) {
        var existing = await ticketRepository.GetByIdAsync(id);
        if (existing == null) return null;

        // Tickets are deleted with their event, so a missing event means the ticket is already gone.
        var ev = await eventRepository.GetByIdAsync(existing.EventId);
        if (ev == null) return null;
        EventAccess.EnsureCanManage(requestId, requestRole, ev);

        // The stock changes with one atomic statement, like a booking does. Saving the whole entity
        // would write back the stock we read earlier and undo any tickets sold in between.
        if (request.TotalQuantity is not null) {
            var delta = request.TotalQuantity.Value - existing.TotalQuantity;
            if (delta != 0 && !await ticketRepository.TryAdjustTotalAsync(id, delta)) {
                throw new ArgumentException("TotalQuantity cannot be lower than the tickets already sold or held.");
            }
        }

        if (request.Type is not null) existing.Type = request.Type;
        if (request.Price is not null) existing.Price = request.Price.Value;

        // No Update() call: EF then writes only the columns changed above, not RemainingQuantity.
        await ticketRepository.SaveChangesAsync();
        await cache.RemoveByPrefixAsync(EventService.CacheKeyPrefix);
        return existing;
    }

    /// <summary>Returns false when the ticket does not exist.</summary>
    /// <exception cref="UnauthorizedAccessException">The caller is neither the event's organizer nor an admin.</exception>
    /// <exception cref="TicketHasSalesException">Some of the ticket was sold or is held by a pending booking.</exception>
    public async Task<bool> DeleteAsync(string requestId, UserRole requestRole, string id) {
        var existing = await ticketRepository.GetByIdAsync(id);
        if (existing == null) return false;

        var ev = await eventRepository.GetByIdAsync(existing.EventId);
        if (ev == null) return false;
        EventAccess.EnsureCanManage(requestId, requestRole, ev);

        // One statement checks "nothing sold" and deletes, so a booking cannot slip in between.
        if (!await ticketRepository.TryDeleteUnsoldAsync(id)) throw new TicketHasSalesException();

        await cache.RemoveByPrefixAsync(EventService.CacheKeyPrefix);
        return true;
    }
}