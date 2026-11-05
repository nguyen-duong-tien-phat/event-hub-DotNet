using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Users;

namespace EventHub.Core.Tickets;

public class TicketService(ITicketRepository ticketRepository, IEventRepository eventRepository) {
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

        if (request.Type is not null) existing.Type = request.Type;
        if (request.Price is not null) existing.Price = request.Price.Value;
        if (request.TotalQuantity is not null) existing.TotalQuantity = request.TotalQuantity.Value;

        ticketRepository.Update(existing);
        await ticketRepository.SaveChangesAsync();
        return existing;
    }
}