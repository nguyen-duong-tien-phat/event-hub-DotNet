using EventHub.Infrastructure.Caching;
using StackExchange.Redis;

namespace EventHub.Tests;

/// <summary>Runs only when a Redis is listening on localhost:6379 (docker compose up -d redis).</summary>
public sealed class RedisFactAttribute : FactAttribute {
    private static readonly Lazy<bool> Available = new(() => {
        try {
            using var redis = ConnectionMultiplexer.Connect("localhost:6379,connectTimeout=500,abortConnect=true");
            return redis.IsConnected;
        }
        catch {
            return false;
        }
    });

    public RedisFactAttribute() {
        if (!Available.Value) Skip = "Redis is not running on localhost:6379";
    }
}

public class RedisRateLimiterTests : IDisposable {
    private readonly ConnectionMultiplexer? _redis;
    private readonly RedisRateLimiter? _limiter;
    private readonly string _key = $"test:ratelimit:{Guid.NewGuid():N}";

    public RedisRateLimiterTests() {
        try {
            _redis = ConnectionMultiplexer.Connect("localhost:6379,connectTimeout=500,abortConnect=true");
            _limiter = new RedisRateLimiter(_redis);
        }
        catch {
            // Every test here is skipped by RedisFact in this case.
        }
    }

    public void Dispose() {
        _redis?.GetDatabase().KeyDelete(_key);
        _redis?.Dispose();
    }

    [RedisFact]
    public async Task AllowsUpToTheLimit_ThenRefuses() {
        for (var i = 0; i < 5; i++) {
            Assert.True(await _limiter!.IsAllowedAsync(_key, 5, TimeSpan.FromMinutes(1)), $"request {i + 1}");
        }

        Assert.False(await _limiter!.IsAllowedAsync(_key, 5, TimeSpan.FromMinutes(1)));
    }

    [RedisFact]
    public async Task RefillsSteadily_SoARefusedCallerIsLetInAgainAfterAWhile() {
        // 2 tokens per second: one token comes back every 500 ms.
        var window = TimeSpan.FromSeconds(1);
        Assert.True(await _limiter!.IsAllowedAsync(_key, 2, window));
        Assert.True(await _limiter.IsAllowedAsync(_key, 2, window));
        Assert.False(await _limiter.IsAllowedAsync(_key, 2, window));

        await Task.Delay(700);

        Assert.True(await _limiter.IsAllowedAsync(_key, 2, window));
        Assert.False(await _limiter.IsAllowedAsync(_key, 2, window));
    }

    [RedisFact]
    public async Task KeysAreIndependent() {
        var other = _key + ":other";
        try {
            Assert.True(await _limiter!.IsAllowedAsync(_key, 1, TimeSpan.FromMinutes(1)));
            Assert.False(await _limiter.IsAllowedAsync(_key, 1, TimeSpan.FromMinutes(1)));

            Assert.True(await _limiter.IsAllowedAsync(other, 1, TimeSpan.FromMinutes(1)));
        }
        finally {
            await _redis!.GetDatabase().KeyDeleteAsync(other);
        }
    }

    [RedisFact]
    public async Task EveryKeyGetsAnExpiry_SoNothingIsLeftBehindForever() {
        await _limiter!.IsAllowedAsync(_key, 5, TimeSpan.FromMinutes(1));

        var ttl = await _redis!.GetDatabase().KeyTimeToLiveAsync(_key);

        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalSeconds, 1, 62);
    }

    [RedisFact]
    public async Task SimultaneousRequests_NeverLetMoreThroughThanTheLimit() {
        var results = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => _limiter!.IsAllowedAsync(_key, 10, TimeSpan.FromMinutes(1))));

        Assert.Equal(10, results.Count(allowed => allowed));
    }
}
