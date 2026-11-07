using EventHub.Core.Bookings;

namespace EventHub.Tests;

public class TicketCodeSignerTests {
    private readonly TicketCodeSigner _signer = new("a-secret-key-long-enough-for-hmac");

    [Fact]
    public void Sign_ThenVerify_GivesBackTheAdmission() {
        var code = _signer.Sign("adm_abc");

        Assert.True(_signer.TryVerify(code, out var admissionId));
        Assert.Equal("adm_abc", admissionId);
    }

    [Fact]
    public void Verify_RejectsAnotherAdmissionsSignature() {
        var parts = _signer.Sign("adm_abc").Split('.');
        var forged = $"{parts[0]}.adm_xyz.{parts[2]}";

        Assert.False(_signer.TryVerify(forged, out _));
    }

    [Fact]
    public void Verify_RejectsACodeSignedWithAnotherSecret() {
        var code = new TicketCodeSigner("a-different-secret-key-for-hmac!!").Sign("adm_abc");

        Assert.False(_signer.TryVerify(code, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("eventhub:ticket:booking_abc:1")]   // the very old unsigned format
    [InlineData("eh1.booking_abc.1.sig")]            // the numbered format
    [InlineData("eh2.adm_abc")]
    [InlineData("eh2.adm_abc.sig.extra")]
    public void Verify_RejectsMalformedCodes(string? code) =>
        Assert.False(_signer.TryVerify(code, out _));
}
