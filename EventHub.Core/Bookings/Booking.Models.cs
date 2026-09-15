using System.ComponentModel.DataAnnotations;

namespace EventHub.Core.Bookings;

public class CreateBookingRequest {
    public required string EventId { get; set; }
    public required string UserId { get; set; }
    public required List<BookingTicketDto> Tickets { get; set; }
}

public class BookingWithPaymentResult {
    public Booking Booking { get; set; } = null!;
    public string ClientSecret { get; set; } = string.Empty;
}

// DTOs
public class BookingTicketDto {
    public required string TicketId { get; set; }
    public int Quantity { get; set; }
}

public class CreateBookingDto {
    [Required]
    public required string EventId { get; set; }
    
    [Required]
    [MinLength(1, ErrorMessage = "At least one ticket is required")]
    public required List<BookingTicketDto> Tickets { get; set; }
}

public class BookingResponseDto {
    public required string Id { get; set; }
    public required List<BookingTicket> Tickets { get; set; }
    public int Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public static BookingResponseDto FromEntity(Booking booking) => new() {
        Id = booking.Id,
        Tickets = booking.Tickets,
        Status = booking.Status.ToString(),
        CreatedAt = booking.CreatedAt
    };
}