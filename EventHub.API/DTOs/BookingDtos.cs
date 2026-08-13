using System.ComponentModel.DataAnnotations;
using EventHub.Core.Entities;

namespace EventHub.DTOs;

public class CreateBookingDto {
    [Required]
    public Guid TicketId { get; set; }
    
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero")]
    public int Quantity { get; set; } = 1;
}

public class BookingResponseDto {
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public int Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public static BookingResponseDto FromEntity(Booking booking) => new() {
        Id = booking.Id,
        TicketId = booking.TicketId,
        Quantity = booking.Quantity,
        Status = booking.Status.ToString(),
        CreatedAt = booking.CreatedAt
    };
}