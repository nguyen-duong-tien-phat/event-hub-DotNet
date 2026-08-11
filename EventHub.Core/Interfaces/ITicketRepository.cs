using EventHub.Core.Entities;

namespace EventHub.Core.Interfaces;

public interface ITicketRepository : IRepository<Ticket> {
    Task<List<Ticket>> GetByEventIdAsync(Guid eventId);
}