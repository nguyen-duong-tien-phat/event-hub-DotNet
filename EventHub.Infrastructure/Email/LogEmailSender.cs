using EventHub.Core.Email;
using Microsoft.Extensions.Logging;

namespace EventHub.Infrastructure.Email;

/// <summary>Used when no SMTP server is configured: the email is written to the log instead.</summary>
public class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender {
    public Task SendAsync(string to, string subject, string body, string key) {
        logger.LogInformation("Email to {To}: {Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
