namespace EventHub.Core.Email;

public interface ISentEmailRepository {
    /// <summary>Records the key. True for exactly one of several callers with the same key.</summary>
    Task<bool> TryClaimAsync(string key);

    /// <summary>Forgets the key, used when sending failed so a retry can claim it again.</summary>
    Task ReleaseAsync(string key);
}

public interface IEmailSender {
    /// <summary>
    /// Sends a plain-text email. Throws if it could not be sent. The key says what the email is about
    /// (for example "booking-confirmed:{id}"); senders that deduplicate send each key once.
    /// </summary>
    Task SendAsync(string to, string subject, string body, string key);
}