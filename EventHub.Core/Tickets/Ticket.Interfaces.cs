using EventHub.Core.Bookings;
using EventHub.Core.Common;

namespace EventHub.Core.Tickets;

public interface ITicketRepository : IRepository<Ticket> {
    Task<List<Ticket>> GetByEventIdAsync(string eventId);
    Task<(List<Ticket> Items, int TotalCount)> GetPagedByEventIdAsync(string ticketId, int page, int pageSize);
    Task<bool> TryReserveAsync(List<BookingTicket> tickets);
    Task ReleaseAsync(List<BookingTicket> tickets);

    /// <summary>Changes the total and the stock by <paramref name="delta"/> in one statement. False when that would leave less than zero in stock.</summary>
    Task<bool> TryAdjustTotalAsync(string ticketId, int delta);

    /// <summary>Deletes the ticket only if none has been sold or held. False if some have.</summary>
    Task<bool> TryDeleteUnsoldAsync(string ticketId);
}