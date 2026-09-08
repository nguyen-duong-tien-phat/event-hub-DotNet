using EventHub.Core.Entities;

namespace EventHub.Core.Interfaces;

public interface ITicketRepository : IRepository<Ticket> {
    Task<List<Ticket>> GetByEventIdAsync(string eventId);
    Task<(List<Ticket> Items, int TotalCount)> GetPagedByEventIdAsync(string ticketId, int page, int pageSize);
    Task<bool> TryReserveAsync(string ticketId, int quantity);
    Task ReleaseAsync(string ticketId, int quantity);
}