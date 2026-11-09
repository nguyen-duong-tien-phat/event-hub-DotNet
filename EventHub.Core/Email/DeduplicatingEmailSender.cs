namespace EventHub.Core.Email;

/// <summary>Sends each email key at most once: a repeat (a redelivered queue message) is dropped.</summary>
public class DeduplicatingEmailSender(ISentEmailRepository sent, IEmailSender inner) : IEmailSender {
    public async Task SendAsync(string to, string subject, string body, string key) {
        if (!await sent.TryClaimAsync(key)) return;
        try {
            await inner.SendAsync(to, subject, body, key);
        } catch {
            await sent.ReleaseAsync(key);   // not sent after all: let the retry claim it again
            throw;
        }
    }
}