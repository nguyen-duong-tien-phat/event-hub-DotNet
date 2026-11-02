using EventHub.Core.Auth;
using Moq;

namespace EventHub.Tests;

public class ExpiredCredentialsCleanerTests {
    private readonly Mock<IRefreshTokenRepository> _tokens = new();
    private readonly Mock<IPasswordResetRepository> _codes = new();

    [Fact]
    public async Task Clean_DeletesAnythingThatExpiredBeforeTheGracePeriod_AndReportsCounts() {
        var now = new DateTime(2030, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var cutoff = now - ExpiredCredentialsCleaner.Grace;
        _tokens.Setup(r => r.DeleteExpiredAsync(cutoff)).ReturnsAsync(7);
        _codes.Setup(r => r.DeleteExpiredAsync(cutoff)).ReturnsAsync(3);

        var (tokens, codes) = await new ExpiredCredentialsCleaner(_tokens.Object, _codes.Object).CleanAsync(now);

        Assert.Equal(7, tokens);
        Assert.Equal(3, codes);
        _tokens.Verify(r => r.DeleteExpiredAsync(cutoff), Times.Once);
        _codes.Verify(r => r.DeleteExpiredAsync(cutoff), Times.Once);
    }
}
