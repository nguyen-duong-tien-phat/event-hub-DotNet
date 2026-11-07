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

    /// <summary>One page of the people admitted by an event's confirmed bookings, with their buyers.</summary>
    Task<(List<EventAdmissionRow> Items, int TotalCount)> GetPagedByEventAsync(string eventId, bool? checkedIn, string? search, int page, int pageSize);
}

public class AdmissionService(IAdmissionRepository admissions, IEventRepository eventRepository) {
    /// <summary>The event's attendee list; null when the event does not exist.</summary>
    /// <exception cref="UnauthorizedAccessException">The caller may not manage this event.</exception>
    public async Task<PagedResult<EventAdmissionRow>?> GetPagedByEventAsync(
        string requestId, UserRole requestRole, string eventId, bool? checkedIn, string? search, int page, int pageSize) {
        var ev = await eventRepository.GetByIdAsync(eventId);
        if (ev == null) return null;
        EventAccess.EnsureCanManage(requestId, requestRole, ev);

        var (items, totalCount) = await admissions.GetPagedByEventAsync(eventId, checkedIn, search, page, pageSize);
        return new PagedResult<EventAdmissionRow> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }
}
