using EventHub.Core.Enums;

namespace EventHub.Core.Entities;

public class Booking: BaseEntity {
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public string? PaymentIntentId { get; set; }
}