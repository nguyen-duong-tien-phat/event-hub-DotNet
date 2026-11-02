namespace EventHub.Core.Auth;

/// <summary>
/// Removes refresh tokens and password reset codes that can never be used again, so those
/// tables do not grow for ever.
/// </summary>
public class ExpiredCredentialsCleaner(IRefreshTokenRepository refreshTokens, IPasswordResetRepository resetCodes) {
    /// <summary>
    /// Kept this long after expiring. Dead rows are harmless, so there is no hurry, and a
    /// little slack avoids racing a request that is checking one right now.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    public async Task<(int Tokens, int Codes)> CleanAsync(DateTime now) {
        var cutoff = now - Grace;
        var tokens = await refreshTokens.DeleteExpiredAsync(cutoff);
        var codes = await resetCodes.DeleteExpiredAsync(cutoff);
        return (tokens, codes);
    }
}
