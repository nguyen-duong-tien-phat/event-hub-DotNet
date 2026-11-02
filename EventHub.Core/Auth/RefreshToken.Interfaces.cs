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
}
