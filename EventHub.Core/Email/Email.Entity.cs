namespace EventHub.Core.Email;

public class SentEmail {
    public required string Key { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}