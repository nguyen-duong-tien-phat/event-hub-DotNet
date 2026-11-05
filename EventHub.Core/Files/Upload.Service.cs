using System.ComponentModel.DataAnnotations;

namespace EventHub.Core.Files;

public enum UploadKind { EventImage, Avatar }

public class UploadRequestDto {
    [Required] public string ContentType { get; set; } = "";
}

/// <param name="UploadUrl">Presigned URL the client PUTs the file to.</param>
/// <param name="FileUrl">Where the file will be readable once uploaded; store this on the entity.</param>
public record UploadDto(string UploadUrl, string FileUrl);

/// <summary>Hands out short-lived upload URLs so clients send files straight to storage, not through the API.</summary>
public class UploadService(IFileStorage storage) {
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(5);

    private static readonly Dictionary<string, string> Images = new() {
        ["image/jpeg"] = "jpg", ["image/png"] = "png", ["image/webp"] = "webp",
    };

    // A new kind of upload is one more line here. shortcut: no size limit (presigned PUT cannot cap it), use presigned POST if needed.
    private static readonly Dictionary<UploadKind, (string Folder, Dictionary<string, string> Types)> Rules = new() {
        [UploadKind.EventImage] = ("events/images", Images),
        [UploadKind.Avatar] = ("users/avatars", Images),
    };

    /// <summary>True when the URL points into this kind's folder of our own storage, i.e. it came from <see cref="CreateUpload"/>.</summary>
    public bool IsUploadedUrl(UploadKind kind, string url) =>
        url.StartsWith(storage.PublicUrl(Rules[kind].Folder + "/"), StringComparison.Ordinal);

    /// <summary>Deletes a file we uploaded. Any other URL (seed data, external links) is left alone.</summary>
    public Task DeleteUploadedAsync(UploadKind kind, string url) =>
        IsUploadedUrl(kind, url) ? storage.DeleteAsync(url[storage.PublicUrl("").Length..]) : Task.CompletedTask;

    /// <exception cref="ArgumentException">The content type is not allowed for this kind.</exception>
    public UploadDto CreateUpload(UploadKind kind, string contentType) {
        var (folder, types) = Rules[kind];
        if (!types.TryGetValue(contentType, out var ext))
            throw new ArgumentException($"Unsupported content type. Allowed: {string.Join(", ", types.Keys)}.");

        var key = $"{folder}/{Guid.NewGuid():N}.{ext}";
        return new UploadDto(storage.CreateUploadUrl(key, contentType, UrlLifetime), storage.PublicUrl(key));
    }
}
