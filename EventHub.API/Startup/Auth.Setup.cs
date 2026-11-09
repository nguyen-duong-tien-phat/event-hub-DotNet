using System.Text;
using EventHub.Core.Auth;
using EventHub.Core.Email;
using EventHub.Core.Users;
using EventHub.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EventHub.Startup;

public static class AuthSetup {
    /// <summary>Tokens (access and refresh), password reset, and JWT bearer authentication.</summary>
    public static void AddAuth(this WebApplicationBuilder builder) {
        var config = builder.Configuration;
        var services = builder.Services;

        var jwtKey = config["Jwt:Key"];
        if (string.IsNullOrEmpty(jwtKey))
            throw new InvalidOperationException("JWT_KEY is not configured.");
        var jwtIssuer = config["Jwt:Issuer"]!;
        var jwtAudience = config["Jwt:Audience"]!;
        var jwtExpiry = int.Parse(config["Jwt:ExpiryMinutes"]!);
        var refreshTokenDays = int.Parse(config["Jwt:RefreshTokenDays"] ?? "30");

        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped(_ => new TokenService(jwtKey, jwtIssuer, jwtAudience, jwtExpiry));

        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped(sp => new RefreshTokenService(
            sp.GetRequiredService<IRefreshTokenRepository>(),
            sp.GetRequiredService<IUserRepository>(),
            sp.GetRequiredService<TokenService>(),
            TimeSpan.FromDays(refreshTokenDays)));

        services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
        services.AddScoped(sp => new PasswordResetService(
            sp.GetRequiredService<IPasswordResetRepository>(),
            sp.GetRequiredService<IUserRepository>(),
            sp.GetRequiredService<IPasswordHasher<User>>(),
            sp.GetRequiredService<IRefreshTokenRepository>(),
            sp.GetRequiredService<IEmailSender>(),
            jwtKey,
            TimeSpan.FromMinutes(30)));

        JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
        services.AddAuthorization();
    }
}
