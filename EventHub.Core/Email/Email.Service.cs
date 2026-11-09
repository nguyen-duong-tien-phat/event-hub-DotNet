using System.Globalization;
using EventHub.Core.Bookings;
using EventHub.Core.Events;
using EventHub.Core.Users;

namespace EventHub.Core.Email;

/// <summary>
/// Every email the app sends, one method each: the subject, the text and the key live here, so a caller just says
/// <c>emails.BookingConfirmedAsync(booking)</c>. The key says what the email is about; a repeat is sent once.
/// A new email is a new method here, nothing else.
/// </summary>
public class EmailService(IUserRepository users, IEventRepository events, IEmailSender sender) {
    /// <summary>The code to reset a password. Throws when it cannot be sent: the caller needs to know.</summary>
    public Task PasswordResetAsync(User user, string code, TimeSpan lifetime, string resetId) =>
        sender.SendAsync(
            user.Email,
            "Your Event Hub reset code",
            $"Your code to reset your Event Hub password is {code}.\n\n" +
            $"It works for {(int)lifetime.TotalMinutes} minutes. If you didn't ask for it, you can ignore this email.",
            $"password-reset:{resetId}");

    /// <summary>Sent to the buyer when a booking is confirmed. Never throws: it must not fail the booking.</summary>
    public async Task BookingConfirmedAsync(Booking booking) {
        // shortcut: a failure here is dropped (Core has no logger); an outbox would keep it, add one if this ever matters.
        try {
            var buyer = await users.GetByIdAsync(booking.UserId);
            var ev = await events.GetByIdAsync(booking.EventId);
            if (buyer == null || ev == null) return;

            var tickets = string.Join("\n", booking.Tickets.Select(t => $"- {t.Quantity} x {t.TicketType}"));
            await sender.SendAsync(
                buyer.Email,
                $"Your tickets for {ev.Title}",
                $"Hi {buyer.FullName},\n\nYour booking for {ev.Title} is confirmed.\n\n" +
                $"When: {ev.StartsAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC\nWhere: {ev.Location}\n\n{tickets}\n\n" +
                $"Total: ${booking.TotalPrice.ToString("0.00", CultureInfo.InvariantCulture)}\n\nYour tickets and their QR codes are in the app.",
                $"booking-confirmed:{booking.Id}");
        } catch { }
    }

    /// <summary>Sent to the buyer when one of their tickets is scanned at the door. Never throws.</summary>
    public async Task CheckedInAsync(Booking booking, Admission admission, Event ev) {
        try {
            var buyer = await users.GetByIdAsync(booking.UserId);
            if (buyer == null) return;

            await sender.SendAsync(
                buyer.Email,
                $"You are in: {ev.Title}",
                $"Hi {buyer.FullName},\n\nYour {admission.TicketType} ticket ({admission.Code}) for {ev.Title} " +
                "was just scanned at the door. If this was not you, contact the organizer.",
                $"checked-in:{admission.Id}");
        } catch { }
    }
}
