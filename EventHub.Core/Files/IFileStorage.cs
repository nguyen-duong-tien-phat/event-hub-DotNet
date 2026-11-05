namespace EventHub.Core.Files;

public interface IFileStorage {
    string CreateUploadUrl(string key, string contentType, TimeSpan ttl);
    string PublicUrl(string key);
}