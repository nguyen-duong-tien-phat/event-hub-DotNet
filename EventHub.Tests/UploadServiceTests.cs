using EventHub.Core.Files;
using Moq;

namespace EventHub.Tests;

public class UploadServiceTests {
    private readonly Mock<IFileStorage> _storage = new();

    private UploadService CreateService() {
        _storage.Setup(s => s.CreateUploadUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns((string key, string _, TimeSpan _) => $"signed://{key}");
        _storage.Setup(s => s.PublicUrl(It.IsAny<string>())).Returns((string key) => $"public://{key}");
        return new UploadService(_storage.Object);
    }

    [Fact]
    public void CreateUpload_WithUnsupportedContentType_Throws() {
        Assert.Throws<ArgumentException>(() => CreateService().CreateUpload(UploadKind.EventImage, "image/svg+xml"));
        _storage.Verify(s => s.CreateUploadUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public void CreateUpload_ForEventImage_UsesEventFolderAndExtension() {
        var result = CreateService().CreateUpload(UploadKind.EventImage, "image/png");

        _storage.Verify(s => s.CreateUploadUrl(
            It.IsRegex(@"^events/images/[0-9a-f]{32}\.png$"), "image/png", It.IsAny<TimeSpan>()), Times.Once);
        Assert.StartsWith("signed://events/images/", result.UploadUrl);
        Assert.StartsWith("public://events/images/", result.FileUrl);
    }

    [Fact]
    public void CreateUpload_ForAvatar_UsesAvatarFolder() {
        var result = CreateService().CreateUpload(UploadKind.Avatar, "image/jpeg");

        Assert.Matches(@"^public://users/avatars/[0-9a-f]{32}\.jpg$", result.FileUrl);
    }

    [Fact]
    public async Task DeleteUploadedAsync_DeletesByKey_ForOurOwnUrl() {
        await CreateService().DeleteUploadedAsync(UploadKind.EventImage, "public://events/images/abc.png");

        _storage.Verify(s => s.DeleteAsync("events/images/abc.png"), Times.Once);
    }

    [Theory]
    [InlineData("https://example.com/cat.jpg")]
    [InlineData("public://users/avatars/abc.jpg")] // ours, but the wrong kind
    public async Task DeleteUploadedAsync_IgnoresEverythingElse(string url) {
        await CreateService().DeleteUploadedAsync(UploadKind.EventImage, url);

        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CreateUpload_GivesEachFileItsOwnKey() {
        var service = CreateService();

        var a = service.CreateUpload(UploadKind.EventImage, "image/webp");
        var b = service.CreateUpload(UploadKind.EventImage, "image/webp");

        Assert.NotEqual(a.FileUrl, b.FileUrl);
    }
}
