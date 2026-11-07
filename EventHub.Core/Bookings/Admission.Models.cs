using EventHub.Core.Common;

namespace EventHub.Core.Bookings;

public class AdmissionStaffDto {
    public required string Id { get; set; }
    public string FullName { get; set; } = string.Empty;
}

/// <summary>A person's way in, as shown in a booking.</summary>
public class AdmissionDto {
    public required string Id { get; set; }
    public string TicketType { get; set; } = string.Empty;

    /// <summary>The value to put in the QR.</summary>
    public string Code { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public DateTime? CheckedInAt { get; set; }
    public AdmissionStaffDto? CheckedInBy { get; set; }
}

public class AdmissionsQuery : PaginationQuery {
    /// <summary>true: only people who got in; false: only those who did not; left out: both.</summary>
    public bool? CheckedIn { get; set; }

    /// <summary>Part of the short code, or of the buyer's name or email.</summary>
    public string? Search { get; set; }
}

/// <summary>A row of an event's attendee list.</summary>
public class EventAdmissionDto {
    public required string Id { get; set; }
    public string TicketType { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public DateTime? CheckedInAt { get; set; }
    public AdmissionStaffDto? CheckedInBy { get; set; }
    public required string BookingId { get; set; }
    public required BookingBuyerDto Buyer { get; set; }
}
