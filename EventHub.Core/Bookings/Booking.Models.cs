using System.ComponentModel.DataAnnotations;

namespace EventHub.Core.Bookings;

public class CreateBookingRequest {
    public required string EventId { get; set; }
    public required string UserId { get; set; }
    public required List<BookingTicketDto> Tickets { get; set; }

    /// <summary>From the Idempotency-Key header. Null means the client did not ask for retry safety.</summary>
    public string? IdempotencyKey { get; set; }
}

public class BookingWithPaymentResult {
    public Booking Booking { get; set; } = null!;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>True when the key was seen before and this is the original booking, not a new one.</summary>
    public bool Replayed { get; set; }
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

public class BookingBuyerDto {
    public required string Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
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

    /// <summary>Who booked. Only filled where the buyer was loaded: the organizer's list of an event's bookings.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BookingBuyerDto? Buyer { get; set; }

    /// <summary>While the booking is pending, the tickets are held until this time.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Signed QR codes, one per ticket, numbered from 1. Empty until the booking is confirmed.</summary>
    public List<string> TicketCodes { get; set; } = [];

    /// <summary>Stripe client secret to pay with. Only set when the booking is created.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientSecret { get; set; }
}
