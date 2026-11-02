using EventHub.Core.Common;

namespace EventHub.Core.Auth;

public interface IRefreshTokenRepository : IRepository<RefreshToken> {
    Task<RefreshToken?> GetByHashAsync(string tokenHash);

    /// <summary>
    /// Revokes the token only if nobody has yet. True for exactly one of several
    /// simultaneous callers, so a token can be rotated only once.
    /// </summary>
    Task<bool> TryRevokeAsync(string id);

    Task RevokeAllForUserAsync(string userId);

    /// <summary>
    /// Deletes tokens that expired before the given time. Revoked tokens that have not expired
    /// are kept on purpose: seeing one used again is how a stolen token is noticed.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTime expiredBefore);
}
