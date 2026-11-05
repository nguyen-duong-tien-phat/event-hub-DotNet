using EventHub.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Npgsql;
using StackExchange.Redis;

namespace EventHub.Tests;

public class HealthCheckTests {
    private static readonly HealthCheckContext Context = new();

    [Fact]
    public async Task Database_WhenPostgresCannotBeReached_IsUnhealthy() {
        // Nothing listens on port 1; the short timeout keeps the test fast.
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=x;Password=x;Database=x;Timeout=2");

        var result = await new DatabaseHealthCheck(dataSource).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task Redis_WhenItAnswersThePing_IsHealthy() {
        var database = new Mock<IDatabase>();
        database.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ReturnsAsync(TimeSpan.FromMilliseconds(1));
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

        var result = await new RedisHealthCheck(redis.Object).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Redis_WhenThePingFails_IsUnhealthy() {
        var database = new Mock<IDatabase>();
        database.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

        var result = await new RedisHealthCheck(redis.Object).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
