using System.Text;
using DotNetEnv;
using EventHub.BackgroundJobs;
using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Payments;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using EventHub.Infrastructure.Bookings;
using EventHub.Infrastructure.Caching;
using EventHub.Infrastructure.Common;
using EventHub.Infrastructure.Data;
using EventHub.Infrastructure.Events;
using EventHub.Infrastructure.Tickets;
using EventHub.Infrastructure.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using Stripe;
using EventService = EventHub.Core.Events.EventService;
using TokenService = EventHub.Core.Auth.TokenService;

Env.Load("../.env");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

var dataSourceBuilder = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("DefaultConnection"));
dataSourceBuilder.EnableDynamicJson();
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(dataSourceBuilder.Build()));

// Stripe
StripeConfiguration.ApiKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
builder.Services.AddScoped<IPaymentService, StripePaymentService>();

// Cache, Rate-limit
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddScoped<ICacheService, RedisCacheService>();
builder.Services.AddScoped<IRateLimiter, RedisRateLimiter>();

// JWT
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;
var jwtExpiry = int.Parse(builder.Configuration["Jwt:ExpiryMinutes"]!);

if (string.IsNullOrEmpty(jwtKey))
    throw new InvalidOperationException("JWT_KEY is not configured.");

builder.Services.AddScoped(_ => new TokenService(jwtKey, jwtIssuer, jwtAudience, jwtExpiry));

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
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

builder.Services.AddHostedService<BookingExpiryJob>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();