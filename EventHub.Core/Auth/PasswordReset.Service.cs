using System.Security.Cryptography;
using System.Text;
using EventHub.Core.Email;
using EventHub.Core.Users;
using Microsoft.AspNetCore.Identity;

namespace EventHub.Core.Auth;

public class PasswordResetService(
    IPasswordResetRepository codes,
    IUserRepository users,
    IPasswordHasher<User> passwordHasher,
    IRefreshTokenRepository refreshTokens,
    EmailService emails,
    string secret,
    TimeSpan lifetime)
{
    public const int MaxGuesses = 5;

    /// <summary>
    /// Emails a new code. Does nothing visible for an unknown email, so the answer never
    /// tells a stranger which emails have an account.
    /// </summary>
    public async Task RequestAsync(string email) {
        var user = await users.GetByEmailAsync(email.Trim());
        if (user == null) return;

        // Only the newest code works.
        await codes.InvalidateAllForUserAsync(user.Id);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var row = new PasswordResetCode {
            UserId = user.Id,
            CodeHash = Hash(user.Id, code),
            ExpiresAt = DateTime.UtcNow + lifetime,
        };
        await codes.AddAsync(row);
        await codes.SaveChangesAsync();

        await emails.PasswordResetAsync(user, code, lifetime, row.Id);
    }

    /// <summary>Sets a new password when the code is right. False for every kind of failure.</summary>
    public async Task<bool> ResetAsync(string email, string code, string newPassword) {
        var user = await users.GetByEmailAsync(email.Trim());
        if (user == null) return false;

        var active = await codes.GetActiveAsync(user.Id);
        if (active == null) return false;

        // Every guess is counted before it is checked, so racing guesses cannot beat the limit.
        if (!await codes.TryCountAttemptAsync(active.Id, MaxGuesses)) return false;

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(user.Id, code.Trim())),
            Encoding.UTF8.GetBytes(active.CodeHash));
        if (!matches) return false;

        if (!await codes.TryConsumeAsync(active.Id)) return false;

        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        users.Update(user);
        await users.SaveChangesAsync();

        // Whoever knew the old password (or stole a session) must not stay signed in.
        await refreshTokens.RevokeAllForUserAsync(user.Id);
        return true;
    }

    private string Hash(string userId, string code) => Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{userId}:{code}")));
}
