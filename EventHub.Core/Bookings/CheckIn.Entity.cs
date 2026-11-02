using EventHub.Core.Common;

namespace EventHub.Core.Bookings;

/// <summary>
/// One row per ticket that has been scanned at the door. The unique (BookingId, Number) pair
/// is what stops a ticket from getting in twice.
/// </summary>
public class TicketCheckIn() : BaseEntity("checkin") {
    public required string BookingId { get; set; }

    /// <summary>The ticket's number inside the booking, from 1.</summary>
    public int Number { get; set; }

    /// <summary>The staff member who scanned it.</summary>
    public required string CheckedInBy { get; set; }
}
