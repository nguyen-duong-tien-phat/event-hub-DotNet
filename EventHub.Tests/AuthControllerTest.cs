using EventHub.Auth;
using EventHub.Core.Auth;
using EventHub.Core.Common;
using EventHub.Core.Email;
using EventHub.Core.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EventHub.Tests;

public class AuthControllerTest {
    private static PasswordResetService NoResetService(Mock<IUserRepository> userRepo) => new(
        new Mock<IPasswordResetRepository>().Object, userRepo.Object,
        new Mock<IPasswordHasher<User>>().Object, new Mock<IRefreshTokenRepository>().Object,
        new Mock<IEmailSender>().Object, "test-secret", TimeSpan.FromMinutes(30));

    [Fact]
    public async Task Login_WhenRateLimited_Returns429() {
        var rateLimiter = new Mock<IRateLimiter>();
        rateLimiter.Setup(r => r.IsAllowedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        
        var userRepo =  new Mock<IUserRepository>();
        var passwordHasher = new Mock<IPasswordHasher<User>>();
        var userService = new UserService(userRepo.Object, passwordHasher.Object);
        var tokenService = new TokenService("dev-secret-key-at-least-32-characters-long", "EventHub", "EventHubUsers", 15);
        var refreshTokens = new RefreshTokenService(
            new Mock<IRefreshTokenRepository>().Object, userRepo.Object, tokenService, TimeSpan.FromDays(30));
        
        var authController = new AuthController(userService, refreshTokens, NoResetService(userRepo), rateLimiter.Object) {
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

    private static AuthController CreateController(
        Mock<IRateLimiter> rateLimiter, Mock<IUserRepository> userRepo) {
        var userService = new UserService(userRepo.Object, new Mock<IPasswordHasher<User>>().Object);
        var tokenService = new TokenService("dev-secret-key-at-least-32-characters-long", "EventHub", "EventHubUsers", 15);
        var refreshTokens = new RefreshTokenService(
            new Mock<IRefreshTokenRepository>().Object, userRepo.Object, tokenService, TimeSpan.FromDays(30));

        return new AuthController(userService, refreshTokens, NoResetService(userRepo), rateLimiter.Object) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public async Task Login_WhenOneAccountIsBeingGuessed_Returns429EvenFromAFreshAddress() {
        var rateLimiter = new Mock<IRateLimiter>();
        // The address is fine, but this account has had too many tries.
        rateLimiter.Setup(r => r.IsAllowedAsync(It.Is<string>(k => k.StartsWith("rateLimit:login:ip:")), It.IsAny<int>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);
        rateLimiter.Setup(r => r.IsAllowedAsync(It.Is<string>(k => k.StartsWith("rateLimit:login:email:")), It.IsAny<int>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        var userRepo = new Mock<IUserRepository>();

        var result = await CreateController(rateLimiter, userRepo)
            .Login(new LoginDto { Email = "Victim@Test.com", Password = "x" });

        Assert.Equal(429, Assert.IsType<ObjectResult>(result).StatusCode);
        userRepo.Verify(r => r.GetByEmailAsync(It.IsAny<string>()), Times.Never);
        // The account is counted whatever the capitals or spaces of the email.
        rateLimiter.Verify(r => r.IsAllowedAsync("rateLimit:login:email:victim@test.com", It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task Register_WhenTheEmailIsTaken_Returns409() {
        var rateLimiter = new Mock<IRateLimiter>();
        rateLimiter.Setup(r => r.IsAllowedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(r => r.TryAddAsync(It.IsAny<User>())).ReturnsAsync(false);

        var result = await CreateController(rateLimiter, userRepo)
            .Register(new RegisterDto { Email = "taken@test.com", FullName = "A", Password = "password123" });

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Register_WhenRateLimited_Returns429AndCreatesNoUser() {
        var rateLimiter = new Mock<IRateLimiter>();
        rateLimiter.Setup(r => r.IsAllowedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        var userRepo = new Mock<IUserRepository>();

        var result = await CreateController(rateLimiter, userRepo)
            .Register(new RegisterDto { Email = "a@b.c", Password = "Passw0rd!x", FullName = "A" });

        Assert.Equal(429, Assert.IsType<ObjectResult>(result).StatusCode);
        userRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Never);
    }
}
