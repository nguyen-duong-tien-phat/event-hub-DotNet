using EventHub.Core.Auth;
using EventHub.Core.Email;
using EventHub.Core.Users;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace EventHub.Tests;

public class PasswordResetServiceTests {
    private readonly Mock<IPasswordResetRepository> _codes = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly Mock<IPasswordHasher<User>> _hasher = new();
    private readonly User _user = new() { Id = "user_1", Email = "a@b.c", FullName = "A", PasswordHash = "old" };
    private readonly List<PasswordResetCode> _stored = [];
    private string? _sentBody;

    public PasswordResetServiceTests() {
        _users.Setup(r => r.GetByEmailAsync("a@b.c")).ReturnsAsync(_user);
        _hasher.Setup(h => h.HashPassword(_user, It.IsAny<string>())).Returns("new-hash");

        _codes.Setup(r => r.AddAsync(It.IsAny<PasswordResetCode>()))
            .Callback<PasswordResetCode>(_stored.Add).Returns(Task.CompletedTask);
        _codes.Setup(r => r.GetActiveAsync("user_1"))
            .ReturnsAsync(() => _stored.LastOrDefault(c => c.UsedAt == null && c.ExpiresAt > DateTime.UtcNow));
        _codes.Setup(r => r.TryCountAttemptAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync((string id, int max) => {
                var code = _stored.First(c => c.Id == id);
                if (code.UsedAt != null || code.Attempts >= max) return false;
                code.Attempts++;
                return true;
            });
        _codes.Setup(r => r.TryConsumeAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => {
                var code = _stored.First(c => c.Id == id);
                if (code.UsedAt != null) return false;
                code.UsedAt = DateTime.UtcNow;
                return true;
            });
        _codes.Setup(r => r.InvalidateAllForUserAsync("user_1"))
            .Callback(() => _stored.ForEach(c => c.UsedAt ??= DateTime.UtcNow))
            .Returns(Task.CompletedTask);

        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((_, _, body, _) => _sentBody = body)
            .Returns(Task.CompletedTask);
    }

    private PasswordResetService CreateService() => new(
        _codes.Object, _users.Object, _hasher.Object, _refreshTokens.Object,
        _email.Object, "test-secret", TimeSpan.FromMinutes(30));

    /// <summary>Asks for a code and reads it out of the email, like the user would.</summary>
    private async Task<string> RequestCodeAsync(PasswordResetService service) {
        await service.RequestAsync("a@b.c");
        return System.Text.RegularExpressions.Regex.Match(_sentBody!, @"\d{6}").Value;
    }

    [Fact]
    public async Task RequestAsync_EmailsASixDigitCode_AndStoresOnlyAHashOfIt() {
        var code = await RequestCodeAsync(CreateService());

        Assert.Matches(@"^\d{6}$", code);
        var row = Assert.Single(_stored);
        Assert.DoesNotContain(code, row.CodeHash);
        Assert.True(row.ExpiresAt > DateTime.UtcNow.AddMinutes(29));
        _email.Verify(e => e.SendAsync("a@b.c", It.IsAny<string>(), It.IsAny<string>(), $"password-reset:{row.Id}"), Times.Once);
    }

    [Fact]
    public async Task RequestAsync_ForAnUnknownEmail_SendsNothingAndSaysNothing() {
        await CreateService().RequestAsync("nobody@example.com");

        Assert.Empty(_stored);
        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RequestAsync_Twice_OnlyTheNewestCodeWorks() {
        var service = CreateService();
        var first = await RequestCodeAsync(service);
        var second = await RequestCodeAsync(service);

        // Two codes in a million could be equal; then the test says nothing.
        if (first == second) return;

        Assert.False(await service.ResetAsync("a@b.c", first, "NewPassw0rd!"));
        Assert.True(await service.ResetAsync("a@b.c", second, "NewPassw0rd!"));
    }

    [Fact]
    public async Task ResetAsync_WithTheRightCode_ChangesThePassword_AndEndsEverySession() {
        var service = CreateService();
        var code = await RequestCodeAsync(service);

        var done = await service.ResetAsync("a@b.c", code, "NewPassw0rd!");

        Assert.True(done);
        Assert.Equal("new-hash", _user.PasswordHash);
        _users.Verify(r => r.SaveChangesAsync(), Times.Once);
        _refreshTokens.Verify(r => r.RevokeAllForUserAsync("user_1"), Times.Once);
    }

    [Fact]
    public async Task ResetAsync_AcceptsTheCodeWithSpacesAround() {
        var service = CreateService();
        var code = await RequestCodeAsync(service);

        Assert.True(await service.ResetAsync(" A@b.c ".ToLowerInvariant(), $" {code} ", "NewPassw0rd!"));
    }

    [Fact]
    public async Task ResetAsync_WithAWrongCode_ChangesNothing() {
        var service = CreateService();
        var code = await RequestCodeAsync(service);
        var wrong = code == "000000" ? "000001" : "000000";

        var done = await service.ResetAsync("a@b.c", wrong, "NewPassw0rd!");

        Assert.False(done);
        Assert.Equal("old", _user.PasswordHash);
        _refreshTokens.Verify(r => r.RevokeAllForUserAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResetAsync_AfterTooManyWrongGuesses_EvenTheRightCodeFails() {
        var service = CreateService();
        var code = await RequestCodeAsync(service);
        var wrong = code == "000000" ? "000001" : "000000";

        for (var i = 0; i < PasswordResetService.MaxGuesses; i++) {
            Assert.False(await service.ResetAsync("a@b.c", wrong, "NewPassw0rd!"));
        }

        Assert.False(await service.ResetAsync("a@b.c", code, "NewPassw0rd!"));
        Assert.Equal("old", _user.PasswordHash);
    }

    [Fact]
    public async Task ResetAsync_ACodeWorksOnlyOnce() {
        var service = CreateService();
        var code = await RequestCodeAsync(service);
        await service.ResetAsync("a@b.c", code, "NewPassw0rd!");

        Assert.False(await service.ResetAsync("a@b.c", code, "AnotherPassw0rd!"));
    }

    [Fact]
    public async Task ResetAsync_WithAnExpiredCode_Fails() {
        var service = CreateService();
        var code = await RequestCodeAsync(service);
        _stored[0].ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        Assert.False(await service.ResetAsync("a@b.c", code, "NewPassw0rd!"));
        Assert.Equal("old", _user.PasswordHash);
    }

    [Fact]
    public async Task ResetAsync_ForAnUnknownEmail_Fails() {
        Assert.False(await CreateService().ResetAsync("nobody@example.com", "123456", "NewPassw0rd!"));
    }

    [Fact]
    public async Task ResetAsync_WhenAnotherRequestUsesTheCodeFirst_Fails() {
        var service = CreateService();
        var code = await RequestCodeAsync(service);
        _codes.Setup(r => r.TryConsumeAsync(It.IsAny<string>())).ReturnsAsync(false);

        Assert.False(await service.ResetAsync("a@b.c", code, "NewPassw0rd!"));
        Assert.Equal("old", _user.PasswordHash);
    }
}
