using EventHub.Core.Users;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace EventHub.Tests;

public class UserServiceTests {
    private readonly Mock<IUserRepository> _userRepo = new();

    private UserService CreateService() => new(_userRepo.Object, new Mock<IPasswordHasher<User>>().Object);

    private User GivenUser(string id, UserRole role) {
        var user = new User { Id = id, Email = $"{id}@mail.test", FullName = id, Role = role };
        _userRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(user);
        return user;
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
}
