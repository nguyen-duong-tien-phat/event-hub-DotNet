using System.Security.Cryptography;
using System.Text;
using EventHub.Core.Users;

namespace EventHub.Core.Auth;

/// <summary>An access token and the refresh token that renews it.</summary>
public class AuthTokens {
    public required User User { get; init; }
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
}

public class RefreshTokenService(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    TokenService tokenService,
    TimeSpan lifetime)
{
    /// <summary>Signs a user in: a new access token and a new refresh token.</summary>
    public async Task<AuthTokens> IssueAsync(User user) {
        var raw = NewRawToken();
        await refreshTokens.AddAsync(new RefreshToken {
            UserId = user.Id,
            TokenHash = Hash(raw),
            ExpiresAt = DateTime.UtcNow + lifetime,
        });
        await refreshTokens.SaveChangesAsync();

        return new AuthTokens { User = user, AccessToken = tokenService.GenerateToken(user), RefreshToken = raw };
    }

    /// <summary>
    /// Trades a refresh token for a new pair. Every refresh token works once: using it
    /// again means it was copied, so all of that user's sessions are ended.
    /// Null when the token is unknown, expired or reused.
    /// </summary>
    public async Task<AuthTokens?> RefreshAsync(string rawToken) {
        var stored = await refreshTokens.GetByHashAsync(Hash(rawToken));
        if (stored == null) return null;

        // Already used or logged out. Someone else may hold a copy: end every session.
        if (stored.RevokedAt != null) {
            await refreshTokens.RevokeAllForUserAsync(stored.UserId);
            return null;
        }

        if (stored.ExpiresAt <= DateTime.UtcNow) return null;

        var user = await users.GetByIdAsync(stored.UserId);
        if (user == null) return null;

        // Two requests with the same token can both get here; only one wins the revoke.
        if (!await refreshTokens.TryRevokeAsync(stored.Id)) {
            await refreshTokens.RevokeAllForUserAsync(stored.UserId);
            return null;
        }

        return await IssueAsync(user);
    }

    /// <summary>Ends one session (logout). Unknown tokens are ignored.</summary>
    public async Task RevokeAsync(string rawToken) {
        var stored = await refreshTokens.GetByHashAsync(Hash(rawToken));
        if (stored != null) await refreshTokens.TryRevokeAsync(stored.Id);
    }

    private static string NewRawToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
