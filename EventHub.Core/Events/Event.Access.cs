using EventHub.Core.Users;

namespace EventHub.Core.Events;

public static class EventAccess {
    /// <summary>Admins and the event's own organizer may change or delete it, and everything attached to it.</summary>
    /// <exception cref="UnauthorizedAccessException">The caller is neither.</exception>
    public static void EnsureCanManage(string requestId, UserRole requestRole, Event ev) {
        var isOwner = requestId == ev.OrganizerId && requestRole == UserRole.Organizer;
        var isAdmin = requestRole == UserRole.Admin;

        if (!isOwner && !isAdmin) {
            throw new UnauthorizedAccessException();
        }
    }
}
