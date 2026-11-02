namespace EventHub.Core.Email;

public interface IEmailSender {
    /// <summary>Sends a plain-text email. Throws if it could not be sent.</summary>
    Task SendAsync(string to, string subject, string body);
}
