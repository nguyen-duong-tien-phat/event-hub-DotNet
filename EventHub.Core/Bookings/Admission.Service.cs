using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Users;

namespace EventHub.Core.Bookings;

public record AdmissionRow(Admission Admission, string? StaffName);
public record EventAdmissionRow(Admission Admission, string? StaffName, User Buyer);

public interface IAdmissionRepository {
    /// <summary>
    /// Makes one admission per ticket of a confirmed booking, unless it already has them. Safe to call twice, even at the
    /// same moment (the webhook and the app's confirm can race): the second call finds them and does nothing.
    /// </summary>
    Task IssueAsync(Booking booking);

    Task<Admission?> FindByIdAsync(string id);
    Task<Admission?> FindByCodeAsync(string code);

    /// <summary>Marks the admission as used. False when it already was, even if another scan does the same at this very moment.</summary>
    Task<bool> TryCheckInAsync(string admissionId, string staffId);

    Task<bool> AnyCheckedInAsync(string bookingId);

    /// <summary>The admissions of these bookings, oldest first, with the name of whoever scanned each.</summary>
    Task<List<AdmissionRow>> ListByBookingIdsAsync(IEnumerable<string> bookingIds);

    /// <summary>One page of the people admitted by an event's confirmed bookings, with their buyers: up to pageSize + 1 rows (the extra shows there is a next page).</summary>
    Task<(List<EventAdmissionRow> Items, int TotalCount)> GetPagedByEventAsync(string eventId, bool? checkedIn, string? search, int page, int pageSize, CursorKey? after = null);
}

public class AdmissionService(IAdmissionRepository admissions, IEventRepository eventRepository) {
    /// <summary>The event's attendee list; null when the event does not exist.</summary>
    /// <exception cref="UnauthorizedAccessException">The caller may not manage this event.</exception>
    public async Task<PagedResult<EventAdmissionRow>?> GetPagedByEventAsync(
        string requestId, UserRole requestRole, string eventId, bool? checkedIn, string? search, int page, int pageSize, string? cursor = null) {
        var after = Cursor.Decode(cursor);
        var ev = await eventRepository.GetByIdAsync(eventId);
        if (ev == null) return null;
        EventAccess.EnsureCanManage(requestId, requestRole, ev);

        var (fetched, totalCount) = await admissions.GetPagedByEventAsync(eventId, checkedIn, search, page, pageSize, after);
        // The list is ordered by when they got in when it shows only those who did, otherwise by when the ticket was made.
        return PagedResult<EventAdmissionRow>.From(fetched, page, pageSize, totalCount,
            r => Cursor.Encode(checkedIn == true ? r.Admission.CheckedInAt!.Value : r.Admission.CreatedAt, r.Admission.Id));
    }
}
