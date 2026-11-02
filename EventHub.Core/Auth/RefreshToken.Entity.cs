using EventHub.Core.Common;

namespace EventHub.Core.Auth;

/// <summary>
/// A long-lived login for one user, used to get a new access token without a password.
/// Only a hash is stored, so a leaked database cannot be used to sign in.
/// </summary>
public class RefreshToken() : BaseEntity("refresh") {
    public required string UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set once the token has been used (rotated) or the user logged out.</summary>
    public DateTime? RevokedAt { get; set; }
}
