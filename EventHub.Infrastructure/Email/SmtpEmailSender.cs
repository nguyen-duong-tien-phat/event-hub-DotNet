using EventHub.Core.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EventHub.Infrastructure.Email;

/// <summary>
/// Sends through any SMTP server: Mailpit while developing, a provider's SMTP in production.
/// STARTTLS is used when the server offers it; a user name turns on sign-in.
/// </summary>
public class SmtpEmailSender(string host, int port, string from, string? user = null, string? password = null)
    : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body) {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, SecureSocketOptions.Auto);
        if (!string.IsNullOrEmpty(user)) await client.AuthenticateAsync(user, password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
