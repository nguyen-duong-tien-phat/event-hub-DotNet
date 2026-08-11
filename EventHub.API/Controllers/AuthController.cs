using EventHub.DTOs;
using EventHub.Core.Services;
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
}