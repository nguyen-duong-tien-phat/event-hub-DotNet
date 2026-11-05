using System.IdentityModel.Tokens.Jwt;
using EventHub.Core.Auth;
using EventHub.Core.Common;
using EventHub.Core.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Auth;


[ApiController]
[Route("auth")]
public class AuthController(
    UserService userService,
    RefreshTokenService refreshTokenService,
    PasswordResetService passwordResetService,
    IRateLimiter rateLimiter) : ControllerBase {
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto) {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Two layers. By address: stops one machine hammering the endpoint. By account:
        // stops guessing one password from many addresses, which the first layer cannot see.
        var allowed =
            await rateLimiter.IsAllowedAsync($"rateLimit:login:ip:{ipAddress}", maxAttempts: 20, window: TimeSpan.FromMinutes(1))
            && await rateLimiter.IsAllowedAsync($"rateLimit:login:email:{dto.Email.Trim().ToLowerInvariant()}", maxAttempts: 10, window: TimeSpan.FromMinutes(15));

        if (!allowed) {
            return StatusCode(429, new { message = "Too many login attempts. Try again later." });
        }

        var user = await userService.VerifyPasswordAsync(dto.Email, dto.Password);
        if (user == null) return Unauthorized(new { message = "Invalid email or password" });
        // Said only after the password matched, so it does not reveal which emails have accounts.
        if (!user.IsActive) return StatusCode(403, new { message = "This account has been blocked." });

        return Ok(ToResponse(await refreshTokenService.IssueAsync(user)));
    }

    /// <summary>Trades a refresh token for a new access token and a new refresh token.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequestDto dto) {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var allowed = await rateLimiter.IsAllowedAsync(
            $"rateLimit:refresh:{ipAddress}", maxAttempts: 30, window: TimeSpan.FromMinutes(1));
        if (!allowed) {
            return StatusCode(429, new { message = "Too many requests. Try again later." });
        }

        var tokens = await refreshTokenService.RefreshAsync(dto.RefreshToken);
        if (tokens == null) return Unauthorized(new { message = "Session expired. Please log in again." });

        return Ok(ToResponse(tokens));
    }

    /// <summary>Ends this session for good. Always succeeds, so logging out never fails.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequestDto dto) {
        await refreshTokenService.RevokeAsync(dto.RefreshToken);
        return NoContent();
    }

    /// <summary>Emails a 6-digit code. Always answers the same, whether or not the email has an account.</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto) {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var allowed =
            await rateLimiter.IsAllowedAsync($"rateLimit:forgot:ip:{ipAddress}", maxAttempts: 10, window: TimeSpan.FromMinutes(10))
            && await rateLimiter.IsAllowedAsync($"rateLimit:forgot:email:{dto.Email.Trim().ToLowerInvariant()}", maxAttempts: 3, window: TimeSpan.FromMinutes(10));
        if (!allowed) {
            return StatusCode(429, new { message = "Too many requests. Try again later." });
        }

        await passwordResetService.RequestAsync(dto.Email);
        return NoContent();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto dto) {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var allowed =
            await rateLimiter.IsAllowedAsync($"rateLimit:reset:ip:{ipAddress}", maxAttempts: 20, window: TimeSpan.FromMinutes(10))
            && await rateLimiter.IsAllowedAsync($"rateLimit:reset:email:{dto.Email.Trim().ToLowerInvariant()}", maxAttempts: 10, window: TimeSpan.FromMinutes(10));
        if (!allowed) {
            return StatusCode(429, new { message = "Too many requests. Try again later." });
        }

        var done = await passwordResetService.ResetAsync(dto.Email, dto.Code, dto.NewPassword);
        return done
            ? NoContent()
            : BadRequest(new { message = "That code is wrong or has expired." });
    }

    private static AuthResponseDto ToResponse(AuthTokens tokens) => new() {
        Id = tokens.User.Id,
        Token = tokens.AccessToken,
        RefreshToken = tokens.RefreshToken,
        Email = tokens.User.Email,
        FullName = tokens.User.FullName,
        Role = tokens.User.Role.ToString()
    };

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto) {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var allowed = await rateLimiter.IsAllowedAsync(
            $"rateLimit:register:{ipAddress}", maxAttempts: 5, window: TimeSpan.FromMinutes(10));
        if (!allowed) {
            return StatusCode(429, new { message = "Too many sign-ups. Try again later." });
        }

        try {
            await userService.CreateAsync(new CreateUserRequest {
                Email = dto.Email,
                FullName = dto.FullName,
                Password = dto.Password,
                Role = UserRole.Attendee
            });
        } catch (EmailTakenException e) {
            return Conflict(new { message = e.Message });
        }

        return Ok();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyInfo() {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var user = await userService.GetByIdAsync(userId);
        if (user  == null) return Unauthorized(new { message = "User not found" });
        return Ok(UserResponseDto.FromEntity(user));
    }
}