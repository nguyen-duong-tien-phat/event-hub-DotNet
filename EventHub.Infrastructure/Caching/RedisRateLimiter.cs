using EventHub.Core.Interfaces;
using StackExchange.Redis;

namespace EventHub.Infrastructure.Caching;

public class RedisRateLimiter(IConnectionMultiplexer redis): IRateLimiter {
    private readonly IDatabase _db = redis.GetDatabase();

    public async Task<bool> IsAllowedAsync(string key, int maxAttempts, TimeSpan window) {
        var count = await _db.StringIncrementAsync(key);

        // key does not exist yet
        if (count == 1) await _db.KeyExpireAsync(key, window);
        
        return count <= maxAttempts;
    }
}