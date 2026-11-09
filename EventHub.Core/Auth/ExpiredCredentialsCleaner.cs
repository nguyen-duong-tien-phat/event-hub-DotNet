using EventHub.Core.Email;

namespace EventHub.Core.Auth;

/// <summary>
/// Removes refresh tokens and password reset codes that can never be used again, and the sent-email keys that no queue
/// message can repeat any more, so those tables do not grow for ever.
/// </summary>
public class ExpiredCredentialsCleaner(IRefreshTokenRepository refreshTokens, IPasswordResetRepository resetCodes, ISentEmailRepository sentEmails) {
    /// <summary>
    /// Kept this long after expiring. Dead rows are harmless, so there is no hurry, and a
    /// little slack avoids racing a request that is checking one right now.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    /// <summary>
    /// How long a sent-email key is kept. It must outlast how long SQS keeps a message (4 days by default),
    /// or a late redelivery would send the email again.
    /// </summary>
    public static readonly TimeSpan SentEmailRetention = TimeSpan.FromDays(30);

    public async Task<(int Tokens, int Codes, int Emails)> CleanAsync(DateTime now) {
        var cutoff = now - Grace;
        var tokens = await refreshTokens.DeleteExpiredAsync(cutoff);
        var codes = await resetCodes.DeleteExpiredAsync(cutoff);
        var emails = await sentEmails.DeleteOlderThanAsync(now - SentEmailRetention);
        return (tokens, codes, emails);
    }
}
