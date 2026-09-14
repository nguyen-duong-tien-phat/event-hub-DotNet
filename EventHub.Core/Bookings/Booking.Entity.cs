using EventHub.Core.Common;
using EventHub.Core.Tickets;
using EventHub.Core.Users;

namespace EventHub.Core.Bookings;

public enum BookingStatus {
    Pending,
    Confirmed,
    Cancelled
}

public class Booking(): BaseEntity("booking") {
    public required  string UserId { get; set; }
    public User? User { get; set; }
    public required string TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public string? PaymentIntentId { get; set; }
}