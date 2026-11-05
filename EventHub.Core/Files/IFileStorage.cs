namespace EventHub.Core.Files;

public interface IFileStorage {
    string CreateUploadUrl(string key, string contentType, TimeSpan ttl);
    string PublicUrl(string key);

    /// <summary>Deletes a file. Best effort: a failure is logged, not thrown, because callers have already committed their own change.</summary>
    Task DeleteAsync(string key);
}