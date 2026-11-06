using EventHub.Core.Auth;
using EventHub.Core.Users;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace EventHub.Tests;

public class UserServiceTests {
    private readonly Mock<IUserRepository> _userRepo = new();

    private readonly Mock<IRefreshTokenRepository> _tokens = new();

    private UserService CreateService() => new(_userRepo.Object, new Mock<IPasswordHasher<User>>().Object, _tokens.Object);

    private User GivenUser(string id, UserRole role) {
        var user = new User { Id = id, Email = $"{id}@mail.test", FullName = id, Role = role };
        _userRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(user);
        return user;
    }

    private static CreateUserRequest SignUp(string email = "An@Mail.Test") =>
        new() { Email = email, FullName = "An", Password = "password123", Role = UserRole.Attendee };

    [Fact]
    public async Task CreateAsync_StoresTheEmailTrimmedAndInLowerCase() {
        _userRepo.Setup(r => r.TryAddAsync(It.IsAny<User>())).ReturnsAsync(true);

        var user = await CreateService().CreateAsync(SignUp("  An@Mail.Test "));

        Assert.Equal("an@mail.test", user.Email);
        _userRepo.Verify(r => r.TryAddAsync(It.Is<User>(u => u.Email == "an@mail.test")), Times.Once);
    }

    [Theory]
    [InlineData(UserRole.Organizer, UserRole.Organizer)]
    [InlineData(UserRole.Admin, UserRole.Admin)]
    [InlineData(null, UserRole.Attendee)] // no role asked for: never anything above Attendee
    public async Task CreateAsync_UsesTheRequestedRole_AndDefaultsToAttendee(UserRole? requested, UserRole expected) {
        _userRepo.Setup(r => r.TryAddAsync(It.IsAny<User>())).ReturnsAsync(true);
        var request = SignUp();
        request.Role = requested;

        var user = await CreateService().CreateAsync(request);

        Assert.Equal(expected, user.Role);
    }

    [Fact]
    public async Task CreateAsync_WhenTheEmailIsAlreadyTaken_Throws() {
        _userRepo.Setup(r => r.TryAddAsync(It.IsAny<User>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<EmailTakenException>(() => CreateService().CreateAsync(SignUp()));
    }

    [Theory]
    [InlineData(UserRole.Organizer)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Attendee)]
    public async Task ChangeRoleAsync_SetsTheRole_AndSavesWithoutRewritingTheWholeRow(UserRole role) {
        var user = GivenUser("user_2", UserRole.Attendee);

        var result = await CreateService().ChangeRoleAsync("admin_1", "user_2", role);

        Assert.Equal(role, result!.Role);
        Assert.Equal(role, user.Role);
        _userRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        _userRepo.Verify(r => r.Update(It.IsAny<User>()), Times.Never); // would also write a stale password hash
    }

    [Theory]
    [InlineData(UserRole.Organizer)]
    [InlineData(UserRole.Attendee)]
    public async Task ChangeRoleAsync_AnAdminLoweringTheirOwnRole_IsRefusedAndNothingChanges(UserRole role) {
        var admin = GivenUser("admin_1", UserRole.Admin);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateService().ChangeRoleAsync("admin_1", "admin_1", role));

        Assert.Equal(UserRole.Admin, admin.Role);
        _userRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task ChangeRoleAsync_AnAdminKeepingTheirOwnAdminRole_IsAllowed() {
        GivenUser("admin_1", UserRole.Admin);

        var result = await CreateService().ChangeRoleAsync("admin_1", "admin_1", UserRole.Admin);

        Assert.Equal(UserRole.Admin, result!.Role);
    }

    [Fact]
    public async Task ChangeRoleAsync_ForAUserThatDoesNotExist_ReturnsNull() {
        Assert.Null(await CreateService().ChangeRoleAsync("admin_1", "missing", UserRole.Organizer));

        _userRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task SetActiveAsync_Blocking_SavesWithoutRewritingTheRow_AndEndsEverySession() {
        var user = GivenUser("user_2", UserRole.Attendee);

        var result = await CreateService().SetActiveAsync("admin_1", "user_2", false);

        Assert.False(result!.IsActive);
        Assert.False(user.IsActive);
        _userRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        _userRepo.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
        _tokens.Verify(t => t.RevokeAllForUserAsync("user_2"), Times.Once);
    }

    [Fact]
    public async Task SetActiveAsync_Unblocking_LeavesTheTokensAlone() {
        var user = GivenUser("user_2", UserRole.Attendee);
        user.IsActive = false;

        var result = await CreateService().SetActiveAsync("admin_1", "user_2", true);

        Assert.True(result!.IsActive);
        _tokens.Verify(t => t.RevokeAllForUserAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SetActiveAsync_AnAdminBlockingThemselves_IsRefusedAndNothingChanges() {
        var admin = GivenUser("admin_1", UserRole.Admin);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService().SetActiveAsync("admin_1", "admin_1", false));

        Assert.True(admin.IsActive);
        _userRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task SetActiveAsync_ForAUserThatDoesNotExist_ReturnsNull() {
        Assert.Null(await CreateService().SetActiveAsync("admin_1", "missing", false));

        _tokens.Verify(t => t.RevokeAllForUserAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetPagedAsync_PassesTheSearchAndTheActiveFilterToTheRepository() {
        _userRepo.Setup(r => r.SearchPagedAsync("an", false, UserRole.Organizer, 2, 5)).ReturnsAsync(([], 0));

        await CreateService().GetPagedAsync(2, 5, "an", isActive: false, role: UserRole.Organizer);

        _userRepo.Verify(r => r.SearchPagedAsync("an", false, UserRole.Organizer, 2, 5), Times.Once);
    }
}
