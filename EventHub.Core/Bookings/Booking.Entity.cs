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
    /// <summary>Total charged: the tickets plus <see cref="ServiceFee"/>.</summary>
    public decimal TotalPrice { get; set; }

    /// <summary>Fee added on top of the tickets (part of <see cref="TotalPrice"/>).</summary>
    public decimal ServiceFee { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public string? PaymentIntentId { get; set; }

    /// <summary>
    /// Client-generated key for the "intent to book". The same key from the same user
    /// always maps to this one booking, so a retry never creates a second one.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

public class BookingTicket {
    public required string TicketId { get; set; }

    /// <summary>Ticket type name at the time of booking, e.g. "VIP".</summary>
    public string TicketType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}