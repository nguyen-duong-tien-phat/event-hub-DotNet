using System.Security.Cryptography;
using System.Text;

namespace EventHub.Core.Bookings;

/// <summary>
/// Makes and checks the code inside a ticket's QR: <c>eh1.{bookingId}.{number}.{signature}</c>.
/// The signature is an HMAC with a server-side secret, so nobody can build a valid code
/// for a booking they do not hold, even if they know its id.
/// </summary>
public class TicketCodeSigner(string secret) {
    private const string Prefix = "eh1";

    // 16 bytes (128 bits) is plenty against forgery and keeps the QR small.
    private const int SignatureBytes = 16;

    public string Sign(string bookingId, int number) =>
        $"{Prefix}.{bookingId}.{number}.{Signature(bookingId, number)}";

    /// <summary>True when the code is well formed and was signed by us.</summary>
    public bool TryVerify(string? code, out string bookingId, out int number) {
        bookingId = string.Empty;
        number = 0;

        var parts = code?.Split('.');
        if (parts is not [Prefix, var id, var num, var signature]) return false;
        if (!int.TryParse(num, out var n) || n < 1) return false;

        // Constant time, so response time does not leak how much of a guess was right.
        var expected = Encoding.UTF8.GetBytes(Signature(id, n));
        var actual = Encoding.UTF8.GetBytes(signature);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return false;

        bookingId = id;
        number = n;
        return true;
    }

    private string Signature(string bookingId, int number) {
        var mac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            // The prefix keeps these signatures apart from any other use of the same key.
            Encoding.UTF8.GetBytes($"ticket:{bookingId}:{number}"));
        return Convert.ToBase64String(mac, 0, SignatureBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
