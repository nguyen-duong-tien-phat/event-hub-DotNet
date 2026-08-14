using System.IdentityModel.Tokens.Jwt;
using EventHub.Core.Enums;
using EventHub.DTOs;
using EventHub.Core.Services;
using EventHub.Core.Services.Models;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(UserService userService, TokenService tokenService) : ControllerBase {
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto) {
        var user = await userService.VerifyPasswordAsync(dto.Email, dto.Password);
        if (user == null) return Unauthorized(new { message = "Invalid email or password" });

        var token = tokenService.GenerateToken(user);
        return Ok(new AuthResponseDto {
            Token = token,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role.ToString()
        });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto) {
        var user = await userService.CreateAsync(new CreateUserRequest {
            Email = dto.Email,
            FullName = dto.FullName,
            Password = dto.Password,
            Role = UserRole.Attendee
        });
        
        return Ok(UserResponseDto.FromEntity(user));
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyInfo() {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var user = await userService.GetByIdAsync(userId);
        if (user  == null) return Unauthorized(new { message = "User not found" });
        return Ok(user);
    }
}