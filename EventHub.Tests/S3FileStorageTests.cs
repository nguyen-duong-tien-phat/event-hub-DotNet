using Amazon.Runtime;
using Amazon.S3;
using EventHub.Infrastructure.Files;

namespace EventHub.Tests;

public class S3FileStorageTests {
    private static S3FileStorage StorageFor(string? serviceUrl) {
        var config = new AmazonS3Config { ForcePathStyle = true, AuthenticationRegion = "us-east-1" };
        if (serviceUrl is not null) config.ServiceURL = serviceUrl;
        else config.RegionEndpoint = Amazon.RegionEndpoint.USEast1;
        return new S3FileStorage(new AmazonS3Client(new BasicAWSCredentials("test", "test"), config), "bucket", "http://files.test/bucket");
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
