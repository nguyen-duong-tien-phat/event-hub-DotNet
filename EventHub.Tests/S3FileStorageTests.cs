using Amazon.Runtime;
using Amazon.S3;
using EventHub.Infrastructure.Files;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EventHub.Tests;

public class S3FileStorageTests {
    private static S3FileStorage StorageFor(string? serviceUrl) {
        var config = new AmazonS3Config { ForcePathStyle = true, AuthenticationRegion = "us-east-1" };
        if (serviceUrl is not null) config.ServiceURL = serviceUrl;
        else config.RegionEndpoint = Amazon.RegionEndpoint.USEast1;
        return new S3FileStorage(new AmazonS3Client(new BasicAWSCredentials("test", "test"), config), "bucket", "http://files.test/bucket",
            NullLogger<S3FileStorage>.Instance);
    }

    [Fact]
    public async Task DeleteAsync_DeletesTheObjectFromTheBucket() {
        var s3 = new Mock<IAmazonS3>();

        await new S3FileStorage(s3.Object, "bucket", "http://files.test/bucket", NullLogger<S3FileStorage>.Instance)
            .DeleteAsync("events/images/a.png");

        s3.Verify(c => c.DeleteObjectAsync("bucket", "events/images/a.png", default), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenS3Fails_DoesNotThrow() {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(c => c.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonClientException("down"));

        var storage = new S3FileStorage(s3.Object, "bucket", "http://files.test/bucket", NullLogger<S3FileStorage>.Instance);

        await storage.DeleteAsync("events/images/a.png"); // the caller already committed; an orphan beats a 500
    }

    [Fact]
    public void CreateUploadUrl_WithPlainHttpEmulator_SignsAnHttpUrl() {
        var url = StorageFor("http://localhost.floci.io:4566").CreateUploadUrl("events/images/a.png", "image/png", TimeSpan.FromMinutes(5));

        Assert.StartsWith("http://localhost.floci.io:4566/bucket/events/images/a.png?", url);
        Assert.Contains("X-Amz-Signature=", url);
    }

    [Fact]
    public void CreateUploadUrl_WithRealAws_SignsAnHttpsUrl() {
        var url = StorageFor(null).CreateUploadUrl("events/images/a.png", "image/png", TimeSpan.FromMinutes(5));

        Assert.StartsWith("https://", url);
    }

    [Fact]
    public void PublicUrl_JoinsBaseAndKeyWithOneSlash() {
        Assert.Equal("http://files.test/bucket/a/b.png", StorageFor(null).PublicUrl("a/b.png"));
    }
}
