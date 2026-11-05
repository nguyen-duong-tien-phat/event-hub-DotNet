namespace EventHub.Core.Events;

public class BookingCounts {
    public int Pending { get; set; }
    public int Confirmed { get; set; }
    public int Cancelled { get; set; }
}

public class TicketStatsDto {
    public required string TicketId { get; set; }
    public required string Type { get; set; }
    public decimal Price { get; set; }
    public int TotalQuantity { get; set; }
    public int RemainingQuantity { get; set; }

    /// <summary>In confirmed (paid) bookings.</summary>
    public int Sold { get; set; }

    /// <summary>Taken out of stock by bookings still waiting for payment.</summary>
    public int Held { get; set; }
}

/// <summary>What the organizer's dashboard shows for one event. Only confirmed bookings count as sales.</summary>
public class EventStatsDto {
    public required string EventId { get; set; }
    public BookingCounts Bookings { get; set; } = new();

    /// <summary>Paid for the tickets themselves, without the service fee. Cancelled (refunded) bookings are not in it.</summary>
    public decimal TicketRevenue { get; set; }
    public decimal ServiceFees { get; set; }
    public int TicketsSold { get; set; }

    /// <summary>People who have been scanned in at the door.</summary>
    public int CheckedIn { get; set; }
    public List<TicketStatsDto> Tickets { get; set; } = [];
}
