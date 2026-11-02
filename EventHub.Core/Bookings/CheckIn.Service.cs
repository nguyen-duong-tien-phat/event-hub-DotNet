using EventHub.Core.Events;
using EventHub.Core.Users;

namespace EventHub.Core.Bookings;

public interface ICheckInRepository {
    /// <summary>
    /// Records the ticket as used. Returns false when it already was, even if another scan
    /// is doing the same at this very moment: the database lets only one of them win.
    /// </summary>
    Task<bool> TryAddAsync(TicketCheckIn checkIn);

    Task<TicketCheckIn?> GetAsync(string bookingId, int number);
}

public enum CheckInStatus {
    Admitted,
    AlreadyUsed,
    /// <summary>Not one of our codes, or it points at a booking or ticket that does not exist.</summary>
    Invalid,
    NotPaid,
    /// <summary>The scanner is not allowed to admit people to this event.</summary>
    Forbidden,
}

public class CheckInResult {
    public CheckInStatus Status { get; init; }
    public string? EventTitle { get; init; }
    public string? TicketType { get; init; }
    public int Number { get; init; }

    /// <summary>For <see cref="CheckInStatus.AlreadyUsed"/>: when it first got in.</summary>
    public DateTime? CheckedInAt { get; init; }
}

public class CheckInService(
    TicketCodeSigner signer,
    IBookingRepository bookingRepository,
    IEventRepository eventRepository,
    ICheckInRepository checkIns)
{
    public async Task<CheckInResult> CheckInAsync(string? code, string staffId, UserRole staffRole) {
        if (!signer.TryVerify(code, out var bookingId, out var number)) {
            return new CheckInResult { Status = CheckInStatus.Invalid };
        }

        // A valid signature only proves we made the code. The booking is still the truth.
        var booking = await bookingRepository.GetByIdAsync(bookingId);
        var type = booking == null ? null : TicketTypeOf(booking, number);
        if (booking == null || type == null) {
            return new CheckInResult { Status = CheckInStatus.Invalid };
        }

        var ev = await eventRepository.GetByIdAsync(booking.EventId);
        if (ev == null) return new CheckInResult { Status = CheckInStatus.Invalid };

        // An organizer only admits people to their own events.
        if (staffRole != UserRole.Admin && ev.OrganizerId != staffId) {
            return new CheckInResult { Status = CheckInStatus.Forbidden };
        }

        var result = (CheckInStatus status, DateTime? at) => new CheckInResult {
            Status = status, EventTitle = ev.Title, TicketType = type, Number = number, CheckedInAt = at,
        };

        if (booking.Status != BookingStatus.Confirmed) return result(CheckInStatus.NotPaid, null);

        var added = await checkIns.TryAddAsync(new TicketCheckIn {
            BookingId = booking.Id, Number = number, CheckedInBy = staffId,
        });
        if (added) return result(CheckInStatus.Admitted, null);

        var first = await checkIns.GetAsync(booking.Id, number);
        return result(CheckInStatus.AlreadyUsed, first?.CreatedAt);
    }

    /// <summary>
    /// Ticket numbers run across the booking's types in order: "2 × General, 1 × VIP" makes
    /// 1 and 2 General, 3 VIP. Null when the number is past the last ticket.
    /// </summary>
    private static string? TicketTypeOf(Booking booking, int number) {
        var seen = 0;
        foreach (var item in booking.Tickets) {
            seen += item.Quantity;
            if (number <= seen) return item.TicketType;
        }
        return null;
    }
}
