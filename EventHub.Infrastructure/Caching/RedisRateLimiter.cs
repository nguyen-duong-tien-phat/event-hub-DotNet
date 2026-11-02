using EventHub.Core.Common;
using StackExchange.Redis;

namespace EventHub.Infrastructure.Caching;

/// <summary>
/// Token bucket in Redis. Each key has a bucket holding up to <c>maxAttempts</c> tokens that
/// refills steadily, so the full amount comes back over one <c>window</c>. A request takes one
/// token; with the bucket empty it is refused. Unlike a counter per time window, a burst
/// cannot be doubled by straddling the end of a window, and the steady rate is the same.
/// </summary>
public class RedisRateLimiter(IConnectionMultiplexer redis): IRateLimiter {
    private readonly IDatabase _db = redis.GetDatabase();

    // One script = one atomic step: no other request can slip in between reading and
    // writing the bucket, and a key can never be left behind without an expiry.
    // The clock is Redis's own, so servers with different clocks agree.
    private const string TakeToken = """
        local capacity = tonumber(ARGV[1])
        local refillPerMs = tonumber(ARGV[2])
        local ttlMs = tonumber(ARGV[3])

        local time = redis.call('TIME')
        local now = time[1] * 1000 + math.floor(time[2] / 1000)

        local bucket = redis.call('HMGET', KEYS[1], 'tokens', 'at')
        local tokens = tonumber(bucket[1])
        local at = tonumber(bucket[2])
        if tokens == nil then
            tokens = capacity
            at = now
        end

        tokens = math.min(capacity, tokens + math.max(0, now - at) * refillPerMs)

        local allowed = 0
        if tokens >= 1 then
            tokens = tokens - 1
            allowed = 1
        end

        redis.call('HSET', KEYS[1], 'tokens', tokens, 'at', now)
        redis.call('PEXPIRE', KEYS[1], ttlMs)
        return allowed
        """;

    public async Task<bool> IsAllowedAsync(string key, int maxAttempts, TimeSpan window) {
        var refillPerMs = maxAttempts / window.TotalMilliseconds;
        // An idle bucket is full again after one window, so it can be forgotten by then.
        var ttlMs = (long)window.TotalMilliseconds + 1000;

        var result = await _db.ScriptEvaluateAsync(
            TakeToken,
            [(RedisKey)key],
            [maxAttempts, refillPerMs, ttlMs]);

        return (int)result == 1;
    }
}
