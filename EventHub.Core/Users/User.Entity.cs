using EventHub.Core.Common;

namespace EventHub.Core.Users;

public enum UserRole {
    Admin,
    Attendee,
    Organizer,
}

public class User(): BaseEntity("user") {
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; } =  UserRole.Attendee; 

    /// <summary>False: the account is blocked from signing in and refreshing (set by an admin).</summary>
    public bool IsActive { get; set; } = true;
}