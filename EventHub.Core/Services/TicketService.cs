using EventHub.Core.Common;
using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Core.Services.Models;

namespace EventHub.Core.Services;

public class TicketService(ITicketRepository ticketRepository) {
    public Task<List<Ticket>> GetByEventIdAsync(Guid eventId) =>
        ticketRepository.GetByEventIdAsync(eventId);

    public Task<Ticket?> GetByIdAsync(Guid id) => ticketRepository.GetByIdAsync(id);
    
    public async Task<PagedResult<Ticket>> GetPagedByEventIdAsync(Guid evenId, int page, int pageSize) {
        var (items, totalCount) = await ticketRepository.GetPagedByEventIdAsync(evenId, page, pageSize);
        return new PagedResult<Ticket> {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<Ticket> CreateAsync(CreateTicketRequest request) {
        var ticket = new Ticket {
            EventId = request.EventId,
            Type = request.Type,
            Price = request.Price,
            TotalQuantity = request.TotalQuantity,
            RemainingQuantity = request.TotalQuantity // starts full, business rule
        };

        await ticketRepository.AddAsync(ticket);
        await ticketRepository.SaveChangesAsync();
        return ticket;
    }

    public async Task<Ticket?> UpdateAsync(Guid id, UpdateTicketRequest request) {
        var existing = await ticketRepository.GetByIdAsync(id);
        if (existing == null) return null;

        if (request.Type is not null) existing.Type = request.Type;
        if (request.Price is not null) existing.Price = request.Price.Value;
        if (request.TotalQuantity is not null) existing.TotalQuantity = request.TotalQuantity.Value;

        ticketRepository.Update(existing);
        await ticketRepository.SaveChangesAsync();
        return existing;
    }
}