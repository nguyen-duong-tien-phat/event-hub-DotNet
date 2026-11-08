using System.Text;
using Amazon.S3;
using DotNetEnv;
using EventHub.BackgroundJobs;
using EventHub.Core.Auth;
using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Email;
using EventHub.Core.Events;
using EventHub.Core.Files;
using EventHub.Core.Payments;
using EventHub.Core.SavedEvents;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using EventHub.Errors;
using EventHub.Health;
using EventHub.Infrastructure.Auth;
using EventHub.Infrastructure.Bookings;
using EventHub.Infrastructure.Caching;
using EventHub.Infrastructure.Common;
using EventHub.Infrastructure.Email;
using EventHub.Infrastructure.Data;
using EventHub.Infrastructure.Events;
using EventHub.Infrastructure.Files;
using EventHub.Infrastructure.SavedEvents;
using EventHub.Infrastructure.Tickets;
using EventHub.Infrastructure.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;
using StackExchange.Redis;
using Stripe;
using EventService = EventHub.Core.Events.EventService;
using TokenService = EventHub.Core.Auth.TokenService;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using System.Text.Json;

Env.Load("../.env");

var builder = WebApplication.CreateBuilder(args);

// Secrets: when Aws:SecretId is set, read them from Secrets Manager. They win over .env.
// shortcut: read once at startup, so a rotated secret needs a restart; use a custom IConfigurationProvider to reload.
var secretId = builder.Configuration["Aws:SecretId"];
if (!string.IsNullOrEmpty(secretId)) {
    var serviceUrl = builder.Configuration["Aws:ServiceUrl"];
    using var secrets = string.IsNullOrEmpty(serviceUrl)
        ? new AmazonSecretsManagerClient()
        : new AmazonSecretsManagerClient(new AmazonSecretsManagerConfig {
            ServiceURL = serviceUrl,
            AuthenticationRegion = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1" });
    var secret = await secrets.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretId });
    var json = secret.SecretString ?? throw new InvalidOperationException($"Secret '{secretId}' has no text value.");
    builder.Configuration.AddInMemoryCollection(JsonSerializer.Deserialize<Dictionary<string, string?>>(json)!);
}

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// Build the data source once. AddDbContext's callback runs for every request, and
// NpgsqlDataSourceBuilder.Build() is not safe to call concurrently: doing it per request
// throws "Parameter 'factory'" as soon as two requests arrive together.
var dataSourceBuilder = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("DefaultConnection"));
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(dataSource));

// Stripe
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];
builder.Services.AddScoped<IPaymentService, StripePaymentService>();

// Cache, Rate-limit
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddScoped<ICacheService, RedisCacheService>();
builder.Services.AddScoped<IRateLimiter, RedisRateLimiter>();

// JWT
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;
var jwtExpiry = int.Parse(builder.Configuration["Jwt:ExpiryMinutes"]!);

if (string.IsNullOrEmpty(jwtKey))
    throw new InvalidOperationException("JWT_KEY is not configured.");

builder.Services.AddScoped(_ => new TokenService(jwtKey, jwtIssuer, jwtAudience, jwtExpiry));

var refreshTokenDays = int.Parse(builder.Configuration["Jwt:RefreshTokenDays"] ?? "30");
var smtpHost = builder.Configuration["Smtp:Host"];
if (!string.IsNullOrEmpty(smtpHost)) {
    builder.Services.AddSingleton<IEmailSender>(new SmtpEmailSender(
        smtpHost,
        int.Parse(builder.Configuration["Smtp:Port"] ?? "25"),
        builder.Configuration["Smtp:From"] ?? "Event Hub <no-reply@eventhub.local>",
        builder.Configuration["Smtp:User"],
        builder.Configuration["Smtp:Password"]));
}
else {
    // No mail server configured: emails are written to the log.
    builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
}

// File storage: S3Url set -> an S3-compatible emulator (Floci); empty -> real AWS with the default credential chain.
var s3Bucket = builder.Configuration["S3:Bucket"];
var s3PublicBaseUrl = builder.Configuration["S3:PublicBaseUrl"];
if (string.IsNullOrEmpty(s3Bucket) || string.IsNullOrEmpty(s3PublicBaseUrl))
    throw new InvalidOperationException("S3__Bucket and S3__PublicBaseUrl are not configured.");
var s3ServiceUrl = builder.Configuration["S3:ServiceUrl"];
builder.Services.AddSingleton<IAmazonS3>(_ => string.IsNullOrEmpty(s3ServiceUrl)
    ? new AmazonS3Client()
    : new AmazonS3Client(new AmazonS3Config {
        ServiceURL = s3ServiceUrl,
        ForcePathStyle = true,
        AuthenticationRegion = builder.Configuration["S3:Region"] ?? "us-east-1",
    }));
builder.Services.AddSingleton<IFileStorage>(sp => new S3FileStorage(
    sp.GetRequiredService<IAmazonS3>(), s3Bucket, s3PublicBaseUrl, sp.GetRequiredService<ILogger<S3FileStorage>>()));
builder.Services.AddSingleton<UploadService>();

builder.Services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
builder.Services.AddScoped(sp => new PasswordResetService(
    sp.GetRequiredService<IPasswordResetRepository>(),
    sp.GetRequiredService<IUserRepository>(),
    sp.GetRequiredService<IPasswordHasher<User>>(),
    sp.GetRequiredService<IRefreshTokenRepository>(),
    sp.GetRequiredService<IEmailSender>(),
    jwtKey,
    TimeSpan.FromMinutes(30)));

builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped(sp => new RefreshTokenService(
    sp.GetRequiredService<IRefreshTokenRepository>(),
    sp.GetRequiredService<IUserRepository>(),
    sp.GetRequiredService<TokenService>(),
    TimeSpan.FromDays(refreshTokenDays)));

JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        options.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// Repositories, Services
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ITicketRepository, TicketRepository>();
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
// Signs the QR codes on tickets. Kept apart from the JWT key so rotating one does not break the other.
var ticketSecret = builder.Configuration["Tickets:Secret"];
if (string.IsNullOrEmpty(ticketSecret))
    throw new InvalidOperationException("Tickets__Secret is not configured.");
builder.Services.AddSingleton(new TicketCodeSigner(ticketSecret));
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<IAdmissionRepository, AdmissionRepository>();
builder.Services.AddScoped<AdmissionService>();
builder.Services.AddScoped<CheckInService>();
builder.Services.AddScoped<ISavedEventRepository, SavedEventRepository>();
builder.Services.AddScoped<SavedEventService>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

builder.Services.AddHostedService<BookingExpiryJob>();
builder.Services.AddScoped<ExpiredCredentialsCleaner>();
builder.Services.AddHostedService<ExpiredCredentialsCleanupJob>();

builder.Services.AddSwaggerGen(options => {
    options.SwaggerDoc("v1", new OpenApiInfo{ Title = "My API", Version = "v1" });

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
var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOrigins)
    .WithHeaders("Authorization", "Content-Type", "Idempotency-Key")
    .AllowAnyMethod()));

// Errors nobody handled become a 500 with a { message, traceId } body (see GlobalExceptionHandler).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails(); // UseExceptionHandler() refuses to start without it, even though our handler answers
// One line per request: method, path, status, time. No query string (it can carry tokens), no headers, no bodies.
builder.Services.AddHttpLogging(options => {
    options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

// Behind a reverse proxy every request would otherwise come from the proxy's address, so all users would
// share one IP rate-limit bucket. The proxy's X-Forwarded-For is trusted only from the addresses listed here
// (loopback is always trusted): anyone else could fake their address and dodge the limits.
string[] ListOf(string key) => (builder.Configuration[key] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.Configure<ForwardedHeadersOptions>(options => {
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var ip in ListOf("ForwardedHeaders:KnownProxies")) options.KnownProxies.Add(IPAddress.Parse(ip));
    foreach (var network in ListOf("ForwardedHeaders:KnownNetworks")) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

// /health/live: the process answers. /health/ready: it can also reach its database and Redis.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(5))
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();

// Configure the HTTP request pipeline.
// Logging sits outside the exception handler so a failed request is logged with its 500.
// (In Development the developer exception page runs first and shows the details instead.)
app.UseForwardedHeaders(); // first, so everything below sees the client's address and scheme
// Probes hit /health every few seconds; they would drown the request log.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpLogging());
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// On in Development, and wherever Swagger:Enabled is true (the compose stack sets it; a container runs as Production).
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled")) {
    app.UseSwagger();
    app.UseSwaggerUI(c => c.EnablePersistAuthorization());
}

app.Run();