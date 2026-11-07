using System.Security.Cryptography;
using System.Text;

namespace EventHub.Core.Bookings;

/// <summary>
/// Makes and checks the code inside a ticket's QR: <c>eh2.{admissionId}.{signature}</c>.
/// The signature is an HMAC with a server-side secret, so nobody can build a valid code
/// for an admission they do not hold, even if they know its id.
/// </summary>
public class TicketCodeSigner(string secret) {
    private const string Prefix = "eh2";

    // 16 bytes (128 bits) is plenty against forgery and keeps the QR small.
    private const int SignatureBytes = 16;

    public string Sign(string admissionId) => $"{Prefix}.{admissionId}.{Signature(admissionId)}";

    /// <summary>True when the code is well formed and was signed by us.</summary>
    public bool TryVerify(string? code, out string admissionId) {
        admissionId = string.Empty;

        var parts = code?.Split('.');
        if (parts is not [Prefix, var id, var signature]) return false;

        // Constant time, so response time does not leak how much of a guess was right.
        var expected = Encoding.UTF8.GetBytes(Signature(id));
        var actual = Encoding.UTF8.GetBytes(signature);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return false;

        admissionId = id;
        return true;
    }

    private string Signature(string admissionId) {
        var mac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            // The prefix keeps these signatures apart from any other use of the same key.
            Encoding.UTF8.GetBytes($"ticket2:{admissionId}"));
        return Convert.ToBase64String(mac, 0, SignatureBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
