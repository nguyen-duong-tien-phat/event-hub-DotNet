using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using EventHub.Core.Files;
using Microsoft.Extensions.Logging;

namespace EventHub.Infrastructure.Files;

/// <summary>Stores files in S3 (or an S3-compatible emulator such as Floci). Signing is local, so no network call is made.</summary>
public class S3FileStorage(IAmazonS3 s3, string bucket, string publicBaseUrl, ILogger<S3FileStorage> logger) : IFileStorage {
    public string CreateUploadUrl(string key, string contentType, TimeSpan ttl) =>
        s3.GetPreSignedURL(new GetPreSignedUrlRequest {
            BucketName = bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            // The SDK signs https:// unless told otherwise, even when ServiceURL is a plain-http emulator.
            Protocol = s3.Config.ServiceURL?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true
                ? Protocol.HTTP : Protocol.HTTPS,
            ContentType = contentType, // signed, so the client must send exactly this Content-Type
            Expires = DateTime.UtcNow.Add(ttl),
        });

    public string PublicUrl(string key) => $"{publicBaseUrl.TrimEnd('/')}/{key}";

    public async Task DeleteAsync(string key) {
        try {
            await s3.DeleteObjectAsync(bucket, key);
        } catch (AmazonClientException e) {
            logger.LogWarning(e, "Could not delete {Key} from bucket {Bucket}; the file is orphaned.", key, bucket);
        }
    }
}
