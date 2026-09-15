using EventHub.Core.Bookings;
using EventHub.Core.Common;

namespace EventHub.Core.Tickets;

public interface ITicketRepository : IRepository<Ticket> {
    Task<List<Ticket>> GetByEventIdAsync(string eventId);
    Task<(List<Ticket> Items, int TotalCount)> GetPagedByEventIdAsync(string ticketId, int page, int pageSize);
    Task<bool> TryReserveAsync(List<BookingTicket> tickets);
    Task ReleaseAsync(List<BookingTicket> tickets);
}