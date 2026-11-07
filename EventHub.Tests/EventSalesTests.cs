using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Files;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

/// <summary>GET /events/sales: whose events a caller may total, and how the repository's numbers become the response.</summary>
public class EventSalesTests {
    private readonly Mock<IEventRepository> _eventRepo = new();

    private EventService CreateService() => new(
        _eventRepo.Object, new Mock<ITicketRepository>().Object, new Mock<IBookingRepository>().Object,
        new Mock<ICacheService>().Object, new UploadService(new Mock<IFileStorage>().Object), new Mock<IUserRepository>().Object);

    private static readonly SalesSummaryDto Summary = new() { Events = 25, TicketsSold = 7, TicketRevenue = 123.45m };

    public EventSalesTests() {
        _eventRepo.Setup(r => r.GetSalesAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync((Summary, new List<EventSalesDto> { new() { Id = "e1", Title = "T", TicketsSold = 7 } }));
    }

    [Theory]
    [InlineData("org_1", "org_1")]
    [InlineData(null, null)] // an admin without a name gets everyone's
    [InlineData("", null)]
    public async Task AnAdmin_MayAskForAnyOrganizerOrForEveryone(string? asked, string? expected) {
        await CreateService().GetSalesAsync("admin_1", UserRole.Admin, asked, 1, 10);

        _eventRepo.Verify(r => r.GetSalesAsync(expected, 1, 10, It.IsAny<DateTime>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("org_1")]
    public async Task AnOrganizer_GetsTheirOwnEvents_WhetherOrNotTheyNameThemselves(string? asked) {
        await CreateService().GetSalesAsync("org_1", UserRole.Organizer, asked, 1, 10);

        _eventRepo.Verify(r => r.GetSalesAsync("org_1", 1, 10, It.IsAny<DateTime>()), Times.Once);
    }

    [Theory]
    [InlineData("org_1", UserRole.Organizer, "org_2")] // someone else's events
    [InlineData("att_1", UserRole.Attendee, null)]
    [InlineData("att_1", UserRole.Attendee, "att_1")]
    public async Task Anyone_Else_IsRefused_AndNothingIsQueried(string userId, UserRole role, string? asked) {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService().GetSalesAsync(userId, role, asked, 1, 10));

        _eventRepo.Verify(r => r.GetSalesAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task TheResponse_CarriesTheSummary_AndAPageWhoseTotalIsTheNumberOfEvents() {
        var sales = await CreateService().GetSalesAsync("admin_1", UserRole.Admin, null, 3, 10);

        Assert.Same(Summary, sales.Summary);
        Assert.Equal(25, sales.Events.TotalCount);
        Assert.Equal(3, sales.Events.TotalPages);
        Assert.Equal((3, 10), (sales.Events.Page, sales.Events.PageSize));
        Assert.Equal("e1", sales.Events.Items.Single().Id);
    }
}
