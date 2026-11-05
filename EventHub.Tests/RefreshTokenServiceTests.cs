using EventHub.Core.Auth;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

public class RefreshTokenServiceTests {
    private readonly Mock<IRefreshTokenRepository> _tokens = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly User _user = new() { Id = "user_1", Email = "a@b.c", FullName = "A" };
    private readonly List<RefreshToken> _stored = [];

    public RefreshTokenServiceTests() {
        _users.Setup(r => r.GetByIdAsync("user_1")).ReturnsAsync(_user);
        _tokens.Setup(r => r.AddAsync(It.IsAny<RefreshToken>()))
            .Callback<RefreshToken>(_stored.Add)
            .Returns(Task.CompletedTask);
        _tokens.Setup(r => r.GetByHashAsync(It.IsAny<string>()))
            .ReturnsAsync((string hash) => _stored.FirstOrDefault(t => t.TokenHash == hash));
        _tokens.Setup(r => r.TryRevokeAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => {
                var token = _stored.First(t => t.Id == id);
                if (token.RevokedAt != null) return false;
                token.RevokedAt = DateTime.UtcNow;
                return true;
            });
    }

    private RefreshTokenService CreateService() => new(
        _tokens.Object, _users.Object,
        new TokenService("dev-secret-key-at-least-32-characters-long", "EventHub", "EventHubUsers", 15),
        TimeSpan.FromDays(30));

    [Fact]
    public async Task IssueAsync_ReturnsBothTokens_AndStoresOnlyAHashOfTheRefreshToken() {
        var tokens = await CreateService().IssueAsync(_user);

        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));
        var row = Assert.Single(_stored);
        Assert.NotEqual(tokens.RefreshToken, row.TokenHash);
        Assert.DoesNotContain(tokens.RefreshToken, row.TokenHash);
        Assert.True(row.ExpiresAt > DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task RefreshAsync_GivesANewPair_AndRetiresTheOldToken() {
        var service = CreateService();
        var first = await service.IssueAsync(_user);

        var second = await service.RefreshAsync(first.RefreshToken);

        Assert.NotNull(second);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(2, _stored.Count);
        Assert.NotNull(_stored[0].RevokedAt);
        Assert.Null(_stored[1].RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_UsingATokenTwice_EndsEverySessionOfTheUser() {
        var service = CreateService();
        var first = await service.IssueAsync(_user);
        await service.RefreshAsync(first.RefreshToken);

        var again = await service.RefreshAsync(first.RefreshToken);

        Assert.Null(again);
        _tokens.Verify(r => r.RevokeAllForUserAsync("user_1"), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_WhenAnotherRequestWonTheRace_EndsEverySessionOfTheUser() {
        var service = CreateService();
        var first = await service.IssueAsync(_user);
        // Looks unused when read, but is revoked by the time we try (a simultaneous request got there first).
        _tokens.Setup(r => r.TryRevokeAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await service.RefreshAsync(first.RefreshToken);

        Assert.Null(result);
        _tokens.Verify(r => r.RevokeAllForUserAsync("user_1"), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_WithAnUnknownToken_ReturnsNull() {
        var result = await CreateService().RefreshAsync("not-a-real-token");

        Assert.Null(result);
        _tokens.Verify(r => r.RevokeAllForUserAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_WithAnExpiredToken_ReturnsNull() {
        var service = CreateService();
        var first = await service.IssueAsync(_user);
        _stored[0].ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        Assert.Null(await service.RefreshAsync(first.RefreshToken));
    }

    [Fact]
    public async Task RefreshAsync_ForADeletedUser_ReturnsNull() {
        var service = CreateService();
        var first = await service.IssueAsync(_user);
        _users.Setup(r => r.GetByIdAsync("user_1")).ReturnsAsync((User?)null);

        Assert.Null(await service.RefreshAsync(first.RefreshToken));
    }

    [Fact]
    public async Task RevokeAsync_MakesTheTokenUnusable() {
        var service = CreateService();
        var first = await service.IssueAsync(_user);

        await service.RevokeAsync(first.RefreshToken);

        Assert.NotNull(_stored[0].RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_WithAnUnknownToken_DoesNothing() {
        await CreateService().RevokeAsync("not-a-real-token");

        _tokens.Verify(r => r.TryRevokeAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_ForABlockedUser_ReturnsNull_AndIssuesNothing() {
        var service = CreateService();
        var first = await service.IssueAsync(_user);
        _user.IsActive = false;

        Assert.Null(await service.RefreshAsync(first.RefreshToken));

        Assert.Single(_stored); // no new refresh token
    }
}
