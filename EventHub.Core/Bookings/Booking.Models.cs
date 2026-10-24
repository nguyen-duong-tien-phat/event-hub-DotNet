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

public class BookingEventDto {
    public required string Id { get; set; }
    public required string Title { get; set; }
    public DateTime StartsAt { get; set; }
    public required string Location { get; set; }
    public required string ImageUrl { get; set; }
}

public class BookingTicketItemDto {
    public required string TicketId { get; set; }
    public string Type { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class BookingResponseDto {
    public required string Id { get; set; }
    public required string EventId { get; set; }
    public BookingEventDto? Event { get; set; }
    public required List<BookingTicketItemDto> Tickets { get; set; }
    public int Quantity { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ServiceFee { get; set; }
    public decimal TotalPrice { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>While the booking is pending, the tickets are held until this time.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Stripe client secret to pay with. Only set when the booking is created.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientSecret { get; set; }
}
