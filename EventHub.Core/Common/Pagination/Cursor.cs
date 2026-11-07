using System.Text;

namespace EventHub.Core.Common;

/// <summary>Where the last row of a page was: its sort time and id. The next page starts right after it.</summary>
public readonly record struct CursorKey(DateTime At, string Id);

public class InvalidCursorException() : ArgumentException("The cursor is not valid.");

/// <summary>
/// The opaque <c>cursor</c> of keyset paging: base64url of "ticks|id". Keyset paging (WHERE sort key is past the last row)
/// stays fast on deep pages where OFFSET reads and throws away every earlier row.
/// </summary>
public static class Cursor {
    public static string Encode(DateTime at, string id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{at.ToUniversalTime().Ticks}|{id}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Null for no cursor; throws <see cref="InvalidCursorException"/> for one we did not make.</summary>
    public static CursorKey? Decode(string? cursor) {
        if (string.IsNullOrEmpty(cursor)) return null;
        try {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('|', 2);
            if (parts.Length != 2 || parts[1].Length == 0) throw new InvalidCursorException();
            return new CursorKey(new DateTime(long.Parse(parts[0]), DateTimeKind.Utc), parts[1]);
        } catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException or OverflowException) {
            throw new InvalidCursorException();
        }
    }
}
