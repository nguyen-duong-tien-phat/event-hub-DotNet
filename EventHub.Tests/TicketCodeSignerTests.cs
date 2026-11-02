using EventHub.Core.Bookings;

namespace EventHub.Tests;

public class TicketCodeSignerTests {
    private readonly TicketCodeSigner _signer = new("a-secret-key-long-enough-for-hmac");

    [Fact]
    public void Sign_ThenVerify_GivesBackTheBookingAndNumber() {
        var code = _signer.Sign("booking_abc", 3);

        Assert.True(_signer.TryVerify(code, out var bookingId, out var number));
        Assert.Equal("booking_abc", bookingId);
        Assert.Equal(3, number);
    }

    [Fact]
    public void Verify_RejectsAChangedTicketNumber() {
        var parts = _signer.Sign("booking_abc", 1).Split('.');
        var forged = $"{parts[0]}.{parts[1]}.2.{parts[3]}";

        Assert.False(_signer.TryVerify(forged, out _, out _));
    }

    [Fact]
    public void Verify_RejectsAnotherBookingsSignature() {
        var parts = _signer.Sign("booking_abc", 1).Split('.');
        var forged = $"{parts[0]}.booking_xyz.1.{parts[3]}";

        Assert.False(_signer.TryVerify(forged, out _, out _));
    }

    [Fact]
    public void Verify_RejectsACodeSignedWithAnotherSecret() {
        var code = new TicketCodeSigner("a-different-secret-key-for-hmac!!").Sign("booking_abc", 1);

        Assert.False(_signer.TryVerify(code, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("eventhub:ticket:booking_abc:1")]   // the old unsigned format
    [InlineData("eh1.booking_abc.1")]
    [InlineData("eh1.booking_abc.x.sig")]
    [InlineData("eh1.booking_abc.0.sig")]
    [InlineData("eh1.booking_abc.1.sig.extra")]
    public void Verify_RejectsMalformedCodes(string? code) =>
        Assert.False(_signer.TryVerify(code, out _, out _));
}
