using EventHub.Core.Interfaces;
using StackExchange.Redis;

namespace EventHub.Infrastructure.Caching;

public class RedisCacheService(IConnectionMultiplexer redis): ICacheService {
    private readonly IDatabase _db = redis.GetDatabase();

    public async Task<string?> GetAsync(string key) {
        var value = await _db.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task SetAsync(string key, string value, TimeSpan expiry) {
        await _db.StringSetAsync(key, value, expiry);
    }
    
    public async Task RemoveAsync(string key) {
        await _db.KeyDeleteAsync(key);
    }
    
    public async Task RemoveByPrefixAsync(string prefix) {
        var server = redis.GetServer(redis.GetEndPoints().First());
        var keys = server.Keys(pattern: $"{prefix}*");
        foreach (var key in keys) {
            await _db.KeyDeleteAsync(key);
        }
    }
}