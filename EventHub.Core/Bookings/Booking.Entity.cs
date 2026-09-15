using EventHub.Core.Common;
using EventHub.Core.Tickets;
using EventHub.Core.Users;

namespace EventHub.Core.Bookings;

public enum BookingStatus {
    Pending,
    Confirmed,
    Cancelled
}

public class Booking() : BaseEntity("booking")
{
    public required string EventId { get; set; }
    public required string UserId { get; set; }
    public User? User { get; set; }
    public List<BookingTicket> Tickets { get; set; } = [];
    public decimal TotalPrice { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public string? PaymentIntentId { get; set; }
}

public class BookingTicket {
    public required string TicketId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}