using EventHub.Core.Entities;

namespace EventHub.Core.Services.Models;

public class CreateBookingRequest {
    public Guid UserId { get; set; }
    public Guid TicketId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class BookingWithPaymentResult {
    public Booking Booking { get; set; } = null!;
    public string ClientSecret { get; set; } = string.Empty;
}