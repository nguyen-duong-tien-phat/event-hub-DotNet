using System.Net;
using EventHub.Errors;
using EventHub.Health;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;

namespace EventHub.Startup;

public static class WebSetup {
    /// <summary>Controllers, Swagger, CORS, error handling, request log, proxy headers, health checks.</summary>
    public static void AddWebDefaults(this WebApplicationBuilder builder) {
        var config = builder.Configuration;
        var services = builder.Services;

        services.AddControllers();

        services.AddSwaggerGen(options => {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });

            // Definition: makes the "Authorize" button appear
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste the JWT only (no 'Bearer ' prefix)"
            });
            // Requirement: actually sends the token with requests
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });

        // Browser clients (the web dashboard) live on another origin. Tokens travel in headers, not cookies, so no credentials.
        var corsOrigins = (config["Cors:AllowedOrigins"] ?? "http://localhost:5173")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(corsOrigins)
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key")
            .AllowAnyMethod()));

        // Errors nobody handled become a 500 with a { message, traceId } body (see GlobalExceptionHandler).
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails(); // UseExceptionHandler() refuses to start without it, even though our handler answers

        // One line per request: method, path, status, time. No query string (it can carry tokens), no headers, no bodies.
        services.AddHttpLogging(options => {
            options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath
                | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
            options.CombineLogs = true;
        });

        // Behind a reverse proxy every request would otherwise come from the proxy's address, so all users would
        // share one IP rate-limit bucket. The proxy's X-Forwarded-For is trusted only from the addresses listed here
        // (loopback is always trusted): anyone else could fake their address and dodge the limits.
        string[] ListOf(string key) => (config[key] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        services.Configure<ForwardedHeadersOptions>(options => {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var ip in ListOf("ForwardedHeaders:KnownProxies")) options.KnownProxies.Add(IPAddress.Parse(ip));
            foreach (var network in ListOf("ForwardedHeaders:KnownNetworks")) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });

        // /health/live: the process answers. /health/ready: it can also reach its database and Redis.
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(5))
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
    }
}
