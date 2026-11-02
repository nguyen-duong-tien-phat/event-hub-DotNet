using EventHub.Core.Common;

namespace EventHub.Core.Auth;

/// <summary>
/// A 6-digit code emailed to someone who forgot their password. Only a keyed hash is
/// stored, and the code dies after a few wrong guesses, so a million possible codes
/// is not a million tries.
/// </summary>
public class PasswordResetCode() : BaseEntity("reset") {
    public required string UserId { get; set; }
    public required string CodeHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }

    /// <summary>Set once the code worked, or was replaced by a newer one.</summary>
    public DateTime? UsedAt { get; set; }
}
