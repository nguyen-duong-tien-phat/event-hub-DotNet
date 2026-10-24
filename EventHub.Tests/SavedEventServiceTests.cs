using EventHub.Core.Events;
using EventHub.Core.SavedEvents;
using Moq;

namespace EventHub.Tests;

public class SavedEventServiceTests {
    private readonly Mock<ISavedEventRepository> _savedRepo = new();
    private readonly Mock<IEventRepository> _eventRepo = new();

    private SavedEventService CreateService() => new(_savedRepo.Object, _eventRepo.Object);

    [Fact]
    public async Task SaveAsync_WhenEventExists_SavesIt() {
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(new Event {
            Id = "event_1", Title = "T", Description = "D", Location = "L", OrganizerId = "o",
            ImageUrl = "u", Highlights = [], StartsAt = DateTime.UtcNow,
        });

        var saved = await CreateService().SaveAsync("user_1", "event_1");

        Assert.True(saved);
        _savedRepo.Verify(r => r.AddAsync("user_1", "event_1"), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WhenEventDoesNotExist_ReturnsFalseAndSavesNothing() {
        _eventRepo.Setup(r => r.GetByIdAsync("nope")).ReturnsAsync((Event?)null);

        var saved = await CreateService().SaveAsync("user_1", "nope");

        Assert.False(saved);
        _savedRepo.Verify(r => r.AddAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_RemovesFromTheUsersList() {
        await CreateService().RemoveAsync("user_1", "event_1");

        _savedRepo.Verify(r => r.RemoveAsync("user_1", "event_1"), Times.Once);
    }
}
