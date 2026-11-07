using System.Security.Cryptography;
using EventHub.Core.Common;

namespace EventHub.Core.Bookings;

/// <summary>
/// One person admitted by a paid booking: made when the booking is confirmed, scanned once at the door.
/// The row is also where "has this ticket been used" lives. (<c>Ticket</c> is a ticket type, so this has another name.)
/// </summary>
public class Admission() : BaseEntity("adm") {
    public required string BookingId { get; set; }

    /// <summary>The ticket type (<c>Ticket.Id</c>).</summary>
    public required string TicketId { get; set; }

    /// <summary>Ticket type name at the time of booking, e.g. "VIP".</summary>
    public string TicketType { get; set; } = string.Empty;

    /// <summary>Short code a person can type when the QR will not scan. Unique.</summary>
    public string Code { get; set; } = NewCode();

    /// <summary>Null until the ticket is scanned.</summary>
    public DateTime? CheckedInAt { get; set; }
    public string? CheckedInBy { get; set; }

    // Crockford base32: no I, L, O or U, so a typed code is hard to get wrong.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int CodeLength = 8;

    /// <summary>8 random characters, 40 bits.</summary>
    public static string NewCode() =>
        string.Create(CodeLength, 0, static (chars, _) => {
            for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });

    /// <summary>What a typed code means: any letter case, spaces and dashes ignored, O read as 0 and I or L as 1. Null when it is not a code.</summary>
    public static string? NormalizeCode(string? typed) {
        var code = new string((typed ?? "").Where(c => c is not (' ' or '-')).ToArray())
            .ToUpperInvariant().Replace('O', '0').Replace('I', '1').Replace('L', '1');
        return code.Length == CodeLength && code.All(Alphabet.Contains) ? code : null;
    }
}
