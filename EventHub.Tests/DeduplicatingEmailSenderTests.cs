using EventHub.Core.Email;
using Moq;

namespace EventHub.Tests;

public class DeduplicatingEmailSenderTests {
    private readonly Mock<ISentEmailRepository> _sent = new();
    private readonly Mock<IEmailSender> _inner = new();

    private DeduplicatingEmailSender CreateSender() => new(_sent.Object, _inner.Object);

    [Fact]
    public async Task SendAsync_ForANewKey_SendsIt() {
        _sent.Setup(r => r.TryClaimAsync("k1")).ReturnsAsync(true);

        await CreateSender().SendAsync("a@b.c", "subject", "body", "k1");

        _inner.Verify(e => e.SendAsync("a@b.c", "subject", "body", "k1"), Times.Once);
        _sent.Verify(r => r.ReleaseAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_ForAKeyAlreadyClaimed_SendsNothing() {
        _sent.Setup(r => r.TryClaimAsync("k1")).ReturnsAsync(false);

        await CreateSender().SendAsync("a@b.c", "subject", "body", "k1");

        _inner.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_WhenSendingFails_ReleasesTheKey_AndRethrows() {
        _sent.Setup(r => r.TryClaimAsync("k1")).ReturnsAsync(true);
        _inner.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSender().SendAsync("a@b.c", "s", "b", "k1"));

        _sent.Verify(r => r.ReleaseAsync("k1"), Times.Once);
    }
}
