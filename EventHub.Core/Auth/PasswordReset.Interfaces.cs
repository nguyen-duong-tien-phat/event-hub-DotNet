using EventHub.Core.Common;

namespace EventHub.Core.Auth;

public interface IPasswordResetRepository : IRepository<PasswordResetCode> {
    /// <summary>The newest code of the user that is unused and not expired.</summary>
    Task<PasswordResetCode?> GetActiveAsync(string userId);

    /// <summary>Counts one guess. False once the code is used up or has had its fill of guesses.</summary>
    Task<bool> TryCountAttemptAsync(string id, int maxAttempts);

    /// <summary>Marks the code used. True for exactly one of several simultaneous callers.</summary>
    Task<bool> TryConsumeAsync(string id);

    Task InvalidateAllForUserAsync(string userId);
}
