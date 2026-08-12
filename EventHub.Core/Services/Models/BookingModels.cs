namespace EventHub.Core.Services.Models;

public class CreateBookingRequest {
    public Guid UserId { get; set; }
    public Guid TicketId { get; set; }
    public int Quantity { get; set; } = 1;
}