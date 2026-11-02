using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

public class EventServiceTests {
    private readonly Mock<IEventRepository> _eventRepo = new();
    private readonly Mock<ITicketRepository> _ticketRepo = new();
    private readonly Mock<ICacheService> _cache = new();

    private EventService CreateService() =>
        new(_eventRepo.Object, _ticketRepo.Object, _cache.Object);

    private static CreateEventRequest NewRequest(Action<CreateEventRequest>? tweak = null) {
        var request = new CreateEventRequest {
            Title = "Neon Nights",
            Description = "A night out.",
            StartsAt = new DateTime(2030, 1, 1, 19, 0, 0, DateTimeKind.Utc),
            Location = "Hà Nội",
            OrganizerId = "user_1",
            ImageUrl = "https://example.com/a.jpg",
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
