namespace EventHub.Core.Interfaces;

public interface IRateLimiter {
    Task<bool> IsAllowedAsync(string key, int maxAttempts, TimeSpan window);
}