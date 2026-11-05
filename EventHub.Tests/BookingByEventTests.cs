using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Payments;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

/// <summary>Who may list the bookings of an event.</summary>
public class BookingByEventTests {
    private readonly Mock<IBookingRepository> _bookingRepo = new();
    private readonly Mock<IEventRepository> _eventRepo = new();

    private BookingService CreateService() => new(
        _bookingRepo.Object, new Mock<ITicketRepository>().Object, _eventRepo.Object,
        new Mock<IUnitOfWork>().Object, new Mock<IPaymentService>().Object);

    public BookingByEventTests() {
        _bookingRepo.Setup(r => r.GetPagedByEventIdAsync("event_1", 1, 10)).ReturnsAsync((new List<Booking>(), 3));
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(new Event {
            Id = "event_1", Title = "T", Description = "D", Location = "L", OrganizerId = "org_1",
            ImageUrl = "u", Highlights = [], StartsAt = DateTime.UtcNow.AddDays(30),
        });
    }

    [Fact]
    public async Task TheEventsOrganizer_SeesTheBookings() {
        var result = await CreateService().GetPagedByEventIdAsync("event_1", "org_1", UserRole.Organizer, 1, 10);

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task AnAdmin_SeesTheBookingsOfAnyEvent() {
        var result = await CreateService().GetPagedByEventIdAsync("event_1", "admin_1", UserRole.Admin, 1, 10);

        Assert.Equal(3, result.TotalCount);
        _eventRepo.Verify(r => r.GetByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("org_2", UserRole.Organizer)] // organizer of another event
    [InlineData("org_1", UserRole.Attendee)]  // right id, wrong role
    public async Task Anyone_Else_IsRefused_AndNothingIsQueried(string userId, UserRole role) {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateService().GetPagedByEventIdAsync("event_1", userId, role, 1, 10));

        _bookingRepo.Verify(r => r.GetPagedByEventIdAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AnOrganizer_AskingAboutAnEventThatDoesNotExist_IsRefused() {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateService().GetPagedByEventIdAsync("missing", "org_1", UserRole.Organizer, 1, 10));
    }
}
