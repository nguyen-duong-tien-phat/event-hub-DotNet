using System.ComponentModel.DataAnnotations;

namespace EventHub.Core.Bookings;

public class CreateBookingRequest {
    public required string UserId { get; set; }
    public required string TicketId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class BookingWithPaymentResult {
    public Booking Booking { get; set; } = null!;
    public string ClientSecret { get; set; } = string.Empty;
}

// DTOs
public class CreateBookingDto {
    [Required(ErrorMessage = "TicketId is required")]
    public required string TicketId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero")]
    public int Quantity { get; set; } = 1;
}

public class BookingResponseDto {
    public required string Id { get; set; }
    public required string TicketId { get; set; }
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