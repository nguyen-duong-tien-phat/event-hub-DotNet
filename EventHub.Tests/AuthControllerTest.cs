using EventHub.Controllers;
using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Core.Services;
using EventHub.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EventHub.Tests;

public class AuthControllerTest {
    [Fact]
    public async Task Login_WhenRateLimited_Returns429() {
        var rateLimiter = new Mock<IRateLimiter>();
        rateLimiter.Setup(r => r.IsAllowedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        
        var userRepo =  new Mock<IUserRepository>();
        var passwordHasher = new Mock<IPasswordHasher<User>>();
        var userService = new UserService(userRepo.Object, passwordHasher.Object);
        var tokenService = new TokenService("dev-secret-key-at-least-32-characters-long", "EventHub", "EventHubUsers", 60);
        
        var authController = new AuthController(userService, tokenService, rateLimiter.Object) {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext()
            }
        };
        var dto = new LoginDto{ Email = "test@test.com", Password = "wrong-password" };
        
        var result = await authController.Login(dto);
        
        var statusResult =  Assert.IsType<ObjectResult>(result);
        Assert.Equal(429, statusResult.StatusCode);
        
        // Confirm password was NEVER checked — rejected before reaching that logic
        userRepo.Verify(r => r.GetByEmailAsync(It.IsAny<string>()), Times.Never);
    }
}