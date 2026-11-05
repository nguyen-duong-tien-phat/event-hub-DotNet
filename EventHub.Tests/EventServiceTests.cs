using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Files;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

public class EventServiceTests {
    private readonly Mock<IEventRepository> _eventRepo = new();
    private readonly Mock<ITicketRepository> _ticketRepo = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IBookingRepository> _bookingRepo = new();
    private readonly Mock<IFileStorage> _storage = new();

    private const string UploadedImage = "https://files.test/events/images/a.jpg";

    private EventService CreateService() {
        _storage.Setup(s => s.PublicUrl(It.IsAny<string>())).Returns((string key) => $"https://files.test/{key}");
        return new(_eventRepo.Object, _ticketRepo.Object, _bookingRepo.Object, _cache.Object, new UploadService(_storage.Object));
    }

    private static CreateEventRequest NewRequest(Action<CreateEventRequest>? tweak = null) {
        var request = new CreateEventRequest {
            Title = "Neon Nights",
            Description = "A night out.",
            StartsAt = new DateTime(2030, 1, 1, 19, 0, 0, DateTimeKind.Utc),
            Location = "Hà Nội",
            OrganizerId = "user_1",
            ImageUrl = UploadedImage,
        };
        tweak?.Invoke(request);
        return request;
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNewFieldsAndOrganizerEmail() {
        var organizer = new User {
            Id = "user_1", Email = "host@eventhub.dev", FullName = "Starlight", Role = UserRole.Organizer,
        };
        _eventRepo.Setup(r => r.GetEventDetailByIdAsync("event_1")).ReturnsAsync(new Event {
            Id = "event_1",
            Title = "Neon Nights",
            Description = "A night out.",
            StartsAt = new DateTime(2030, 1, 1, 19, 0, 0, DateTimeKind.Utc),
            Location = "Hà Nội",
            OrganizerId = organizer.Id,
            Organizer = organizer,
            ImageUrl = "https://example.com/a.jpg",
            Highlights = [],
            Category = "Concert",
            EndsAt = new DateTime(2030, 1, 1, 23, 0, 0, DateTimeKind.Utc),
            DoorsOpenAt = new DateTime(2030, 1, 1, 18, 0, 0, DateTimeKind.Utc),
            AgeRestriction = "18+ with ID",
            RefundPolicy = "If cancelled",
        });
        _ticketRepo.Setup(r => r.GetByEventIdAsync("event_1")).ReturnsAsync([]);

        var result = await CreateService().GetByIdAsync("event_1");

        Assert.NotNull(result);
        Assert.Equal("host@eventhub.dev", result.Organizer.Email);
        Assert.Equal("Concert", result.Category);
        Assert.Equal(new DateTime(2030, 1, 1, 23, 0, 0, DateTimeKind.Utc), result.EndsAt);
        Assert.Equal("18+ with ID", result.AgeRestriction);
        Assert.Equal("If cancelled", result.RefundPolicy);
    }

    [Fact]
    public void ListItem_DoesNotExposeOrganizerEmail() {
        var organizer = new User { Id = "user_1", Email = "host@eventhub.dev", FullName = "Starlight" };

        var dto = EventListItemDto.FromEntity(new Event {
            Title = "T", Description = "D", StartsAt = DateTime.UtcNow, Location = "L",
            OrganizerId = organizer.Id, Organizer = organizer, ImageUrl = "u", Highlights = [],
            Category = "Concert",
        });

        Assert.Null(dto.Organizer.Email);
        Assert.Equal("Concert", dto.Category);
    }

    [Fact]
    public async Task CreateAsync_WhenEventEndsBeforeItStarts_Throws() {
        var request = NewRequest(r => r.EndsAt = r.StartsAt.AddHours(-1));

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().CreateAsync(request));
        _eventRepo.Verify(r => r.AddAsync(It.IsAny<Event>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenImageIsNotFromOurStorage_Throws() {
        var request = NewRequest(r => r.ImageUrl = "https://evil.test/a.jpg");

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().CreateAsync(request));
        _eventRepo.Verify(r => r.AddAsync(It.IsAny<Event>()), Times.Never);
    }

    private static Event ExistingEvent() => new() {
        Id = "event_1", Title = "T", Description = "D", StartsAt = DateTime.UtcNow.AddDays(30),
        Location = "L", OrganizerId = "user_1", ImageUrl = "old", Highlights = [],
    };

    [Fact]
    public async Task UpdateAsync_WithUploadedImage_ReplacesTheImage() {
        var existing = ExistingEvent();
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(existing);

        await CreateService().UpdateAsync("user_1", UserRole.Organizer, "event_1",
            new UpdateEventRequest { ImageUrl = UploadedImage });

        Assert.Equal(UploadedImage, existing.ImageUrl);
    }

    [Fact]
    public async Task UpdateAsync_WithForeignImage_ThrowsAndKeepsTheOldOne() {
        var existing = ExistingEvent();
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(existing);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().UpdateAsync(
            "user_1", UserRole.Organizer, "event_1", new UpdateEventRequest { ImageUrl = "https://evil.test/a.jpg" }));

        Assert.Equal("old", existing.ImageUrl);
    }

    [Fact]
    public async Task UpdateAsync_WithoutImage_LeavesTheImageAlone() {
        var existing = ExistingEvent();
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(existing);

        await CreateService().UpdateAsync("user_1", UserRole.Organizer, "event_1",
            new UpdateEventRequest { Title = "New title" });

        Assert.Equal("old", existing.ImageUrl);
    }

    [Fact]
    public async Task GetPagedAsync_WithAnOrganizer_AsksTheRepositoryForThatOrganizersEvents() {
        _eventRepo.Setup(r => r.GetEventsQuery(1, 10, null, null, null, null, null, null, "org_1"))
            .ReturnsAsync((new List<Event>(), 0));

        await CreateService().GetPagedAsync(1, 10, null, null, null, null, null, null, "org_1");

        _eventRepo.Verify(r => r.GetEventsQuery(1, 10, null, null, null, null, null, null, "org_1"), Times.Once);
    }

    [Fact]
    public async Task GetPagedAsync_CachesEachOrganizersListSeparately() {
        var keys = new List<string>();
        _cache.Setup(c => c.GetAsync(It.IsAny<string>())).Callback<string>(keys.Add).ReturnsAsync((string?)null);
        _eventRepo.Setup(r => r.GetEventsQuery(It.IsAny<int>(), It.IsAny<int>(), null, null, null, null, null, null, It.IsAny<string?>()))
            .ReturnsAsync((new List<Event>(), 0));
        var service = CreateService();

        await service.GetPagedAsync(1, 10, null, null, null, null, null, null, "org_1");
        await service.GetPagedAsync(1, 10, null, null, null, null, null, null, "org_2");
        await service.GetPagedAsync(1, 10, null, null, null, null, null, null);

        Assert.Equal(3, keys.Distinct().Count());
    }

    [Fact]
    public async Task DeleteAsync_ByTheOrganizer_DeletesTheEventAndItsPoster() {
        var existing = ExistingEvent();
        existing.ImageUrl = UploadedImage;
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(existing);

        var deleted = await CreateService().DeleteAsync("user_1", UserRole.Organizer, "event_1");

        Assert.True(deleted);
        _eventRepo.Verify(r => r.Delete(existing), Times.Once);
        _eventRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        _cache.Verify(c => c.RemoveByPrefixAsync("events:"), Times.Once);
        _storage.Verify(s => s.DeleteAsync("events/images/a.jpg"), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ByAnAdmin_Deletes() {
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(ExistingEvent());

        Assert.True(await CreateService().DeleteAsync("someone_else", UserRole.Admin, "event_1"));
    }

    [Fact]
    public async Task DeleteAsync_ByAnotherOrganizer_ThrowsBeforeTouchingAnything() {
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(ExistingEvent());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateService().DeleteAsync("other_org", UserRole.Organizer, "event_1"));

        _bookingRepo.Verify(r => r.AnyForEventAsync(It.IsAny<string>()), Times.Never);
        _eventRepo.Verify(r => r.Delete(It.IsAny<Event>()), Times.Never);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenTheEventHasBookings_ThrowsAndKeepsEverything() {
        var existing = ExistingEvent();
        existing.ImageUrl = UploadedImage;
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(existing);
        _bookingRepo.Setup(r => r.AnyForEventAsync("event_1")).ReturnsAsync(true);

        await Assert.ThrowsAsync<EventHasBookingsException>(
            () => CreateService().DeleteAsync("user_1", UserRole.Organizer, "event_1"));

        _eventRepo.Verify(r => r.Delete(It.IsAny<Event>()), Times.Never);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenTheEventDoesNotExist_ReturnsFalse() {
        Assert.False(await CreateService().DeleteAsync("user_1", UserRole.Organizer, "missing"));
    }

    [Fact]
    public async Task DeleteAsync_WithAnImageWeDidNotUpload_LeavesTheFileAlone() {
        var existing = ExistingEvent(); // ImageUrl = "old", not under our storage
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(existing);

        await CreateService().DeleteAsync("user_1", UserRole.Organizer, "event_1");

        _eventRepo.Verify(r => r.Delete(existing), Times.Once);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenDoorsOpenAfterStart_Throws() {
        var request = NewRequest(r => r.DoorsOpenAt = r.StartsAt.AddMinutes(10));

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().CreateAsync(request));
    }

    [Theory]
    [InlineData("  Concert ", "Concert")]
    [InlineData("", "Other")]
    [InlineData("   ", "Other")]
    public async Task CreateAsync_NormalizesCategory(string given, string expected) {
        var created = await CreateService().CreateAsync(NewRequest(r => r.Category = given));

        Assert.Equal(expected, created.Category);
    }

    [Fact]
    public void ListItem_ShowsThePriceOfTheCheapestTicket() {
        var ev = new Event {
            Title = "T", Description = "D", StartsAt = DateTime.UtcNow, Location = "L",
            OrganizerId = "o", Organizer = new User { Id = "o", FullName = "Host" }, ImageUrl = "u", Highlights = [],
            Tickets = [
                new Ticket { EventId = "e", Price = 80m, MaxPerOrder = 4 },
                new Ticket { EventId = "e", Price = 25m, MaxPerOrder = 4 },
            ],
        };

        Assert.Equal(25m, EventListItemDto.FromEntity(ev).MinPrice);
    }

    [Fact]
    public void ListItem_WithoutTickets_HasNoPrice() {
        var ev = new Event {
            Title = "T", Description = "D", StartsAt = DateTime.UtcNow, Location = "L",
            OrganizerId = "o", Organizer = new User { Id = "o", FullName = "Host" }, ImageUrl = "u", Highlights = [],
        };

        Assert.Null(EventListItemDto.FromEntity(ev).MinPrice);
    }

    [Fact]
    public async Task GetCategoriesAsync_ReturnsWhatTheRepositoryFindsForUpcomingEvents() {
        _eventRepo.Setup(r => r.GetUpcomingCategoriesAsync()).ReturnsAsync(["Art", "Concert"]);

        var categories = await CreateService().GetCategoriesAsync();

        Assert.Equal(["Art", "Concert"], categories);
    }
}
