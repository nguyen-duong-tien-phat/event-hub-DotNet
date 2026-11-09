using Amazon.S3;
using EventHub.Core.Files;
using EventHub.Infrastructure.Files;

namespace EventHub.Startup;

public static class FilesSetup {
    /// <summary>S3:ServiceUrl set -> an S3-compatible emulator (Floci); empty -> real AWS with the default credential chain.</summary>
    public static void AddFileStorage(this WebApplicationBuilder builder) {
        var config = builder.Configuration;
        var bucket = config["S3:Bucket"];
        var publicBaseUrl = config["S3:PublicBaseUrl"];
        if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(publicBaseUrl))
            throw new InvalidOperationException("S3__Bucket and S3__PublicBaseUrl are not configured.");

        var serviceUrl = config["S3:ServiceUrl"];
        builder.Services.AddSingleton<IAmazonS3>(_ => string.IsNullOrEmpty(serviceUrl)
            ? new AmazonS3Client()
            : new AmazonS3Client(new AmazonS3Config {
                ServiceURL = serviceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = config["S3:Region"] ?? "us-east-1",
            }));
        builder.Services.AddSingleton<IFileStorage>(sp => new S3FileStorage(
            sp.GetRequiredService<IAmazonS3>(), bucket, publicBaseUrl, sp.GetRequiredService<ILogger<S3FileStorage>>()));
        builder.Services.AddSingleton<UploadService>();
    }
}
