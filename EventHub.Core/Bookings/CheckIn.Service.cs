using EventHub.Core.Email;
using EventHub.Core.Events;
using EventHub.Core.Users;

namespace EventHub.Core.Bookings;

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
    public string? AdmissionId { get; init; }
    public string? ShortCode { get; init; }

    /// <summary>When the ticket got in: now for <see cref="CheckInStatus.Admitted"/>, the first scan for <see cref="CheckInStatus.AlreadyUsed"/>.</summary>
    public DateTime? CheckedInAt { get; init; }
}

public class CheckInService(
    TicketCodeSigner signer,
    IBookingRepository bookingRepository,
    IEventRepository eventRepository,
    IAdmissionRepository admissions,
    EmailService? emails = null)
{
    /// <param name="code">The QR value, or the short code typed by hand.</param>
    public async Task<CheckInResult> CheckInAsync(string? code, string staffId, UserRole staffRole) {
        var admission = signer.TryVerify(code?.Trim(), out var id) ? await admissions.FindByIdAsync(id)
            : Admission.NormalizeCode(code) is { } typed ? await admissions.FindByCodeAsync(typed)
            : null;
        if (admission == null) return new CheckInResult { Status = CheckInStatus.Invalid };

        // A valid code only proves we made it. The booking is still the truth.
        var booking = await bookingRepository.GetByIdAsync(admission.BookingId);
        var ev = booking == null ? null : await eventRepository.GetByIdAsync(booking.EventId);
        if (booking == null || ev == null) return new CheckInResult { Status = CheckInStatus.Invalid };

        // An organizer only admits people to their own events.
        if (staffRole != UserRole.Admin && ev.OrganizerId != staffId) {
            return new CheckInResult { Status = CheckInStatus.Forbidden };
        }

        var result = (CheckInStatus status, DateTime? at) => new CheckInResult {
            Status = status, EventTitle = ev.Title, TicketType = admission.TicketType,
            AdmissionId = admission.Id, ShortCode = admission.Code, CheckedInAt = at,
        };

        if (booking.Status != BookingStatus.Confirmed) return result(CheckInStatus.NotPaid, null);

        if (await admissions.TryCheckInAsync(admission.Id, staffId)) {
            if (emails != null) await emails.CheckedInAsync(booking, admission, ev);
            return result(CheckInStatus.Admitted, DateTime.UtcNow);
        }

        var first = await admissions.FindByIdAsync(admission.Id);
        return result(CheckInStatus.AlreadyUsed, first?.CheckedInAt);
    }
}
