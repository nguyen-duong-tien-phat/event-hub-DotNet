using EventHub.Core.Common;
using EventHub.Core.Payments;
using EventHub.Infrastructure.Bookings;
using EventHub.Infrastructure.Caching;
using EventHub.Infrastructure.Common;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;
using Stripe;

namespace EventHub.Startup;

public static class DataSetup {
    public static void AddDatabase(this WebApplicationBuilder builder) {
        // Build the data source once. AddDbContext's callback runs for every request, and
        // NpgsqlDataSourceBuilder.Build() is not safe to call concurrently: doing it per request
        // throws "Parameter 'factory'" as soon as two requests arrive together.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("DefaultConnection"));
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();
        builder.Services.AddSingleton(dataSource);
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(dataSource));
    }

    /// <summary>Cache and rate limiter.</summary>
    public static void AddRedis(this WebApplicationBuilder builder) {
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
        builder.Services.AddScoped<ICacheService, RedisCacheService>();
        builder.Services.AddScoped<IRateLimiter, RedisRateLimiter>();
    }

    public static void AddPayments(this WebApplicationBuilder builder) {
        StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];
        builder.Services.AddScoped<IPaymentService, StripePaymentService>();
    }
}
