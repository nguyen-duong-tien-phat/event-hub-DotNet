namespace EventHub.Core.Entities;

public static class IdGenerator {
    public static string Generate(string prefix) {
        var random = Guid.NewGuid().ToString("N")[..16];
        return $"{prefix}_{random}";
    }
}

public class BaseEntity(string prefix) {
    public string Id { get; set; } = IdGenerator.Generate(prefix);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }
}