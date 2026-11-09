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

/// <summary>Totals over the events a sales request covers. Same meaning as <see cref="EventStatsDto"/>, summed.</summary>
public class SalesSummaryDto {
    public int Events { get; set; }
    public int Upcoming { get; set; }
    public int Past { get; set; }
    public int TicketsSold { get; set; }
    public decimal TicketRevenue { get; set; }
    public int CheckedIn { get; set; }

    /// <summary>Seats of the events that have already started.</summary>
    public int TicketsOfPastEvents { get; set; }

    /// <summary>Tickets sold for the events that have already started: whom a check-in count can be compared with.</summary>
    public int TicketsSoldOfPastEvents { get; set; }
}

/// <summary>One event's line in the sales list. Same meaning as <see cref="EventStatsDto"/>.</summary>
public class EventSalesDto {
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public string Location { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public int RemainingQuantity { get; set; }
    public int TicketsSold { get; set; }
    public decimal TicketRevenue { get; set; }
    public int CheckedIn { get; set; }
}

public class SalesDto {
    public required SalesSummaryDto Summary { get; set; }
    public required Common.PagedResult<EventSalesDto> Events { get; set; }
}

public class SalesQuery : Common.PaginationQuery {
    /// <summary>An admin: whose events (left out: everyone's). An organizer: only themselves.</summary>
    public string? OrganizerId { get; set; }
}
