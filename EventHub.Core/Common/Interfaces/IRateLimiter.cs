namespace EventHub.Core.Common;

public interface IRateLimiter {
    Task<bool> IsAllowedAsync(string key, int maxAttempts, TimeSpan window);
}