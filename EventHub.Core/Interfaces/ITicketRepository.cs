using EventHub.Core.Entities;

namespace EventHub.Core.Interfaces;

public interface ITicketRepository : IRepository<Ticket> {
    Task<List<Ticket>> GetByEventIdAsync(Guid eventId);
    Task<(List<Ticket> Items, int TotalCount)> GetPagedByEventIdAsync(Guid ticketId, int page, int pageSize);
    Task<bool> TryReserveAsync(Guid ticketId, int quantity);
}