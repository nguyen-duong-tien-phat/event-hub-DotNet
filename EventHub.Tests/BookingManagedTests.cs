using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Payments;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

/// <summary>The bookings across events that an admin or an organizer manages.</summary>
public class BookingManagedTests {
    private readonly Mock<IBookingRepository> _bookingRepo = new();
    private readonly Mock<IEventRepository> _eventRepo = new();

    private BookingService CreateService() => new(
        _bookingRepo.Object, new Mock<ITicketRepository>().Object, _eventRepo.Object,
        new Mock<IUnitOfWork>().Object, new Mock<IPaymentService>().Object);

    public BookingManagedTests() {
        _bookingRepo.Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<BookingStatus?>(), 1, 10))
            .ReturnsAsync((new List<Booking>(), 7));
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(new Event {
            Id = "event_1", Title = "T", Description = "D", Location = "L", OrganizerId = "org_1",
            ImageUrl = "u", Highlights = [], StartsAt = DateTime.UtcNow.AddDays(30),
        });
    }

    [Fact]
    public async Task AnAdmin_SeesEveryBooking_NotLimitedToAnOrganizer() {
        var result = await CreateService().GetManagedPagedAsync("admin_1", UserRole.Admin, null, null, 1, 10);

        Assert.Equal(7, result.TotalCount);
        _bookingRepo.Verify(r => r.GetPagedAsync(null, null, null, 1, 10), Times.Once);
    }

    [Fact]
    public async Task AnOrganizer_IsLimitedToTheirOwnEvents() {
        await CreateService().GetManagedPagedAsync("org_1", UserRole.Organizer, null, BookingStatus.Pending, 1, 10);

        _bookingRepo.Verify(r => r.GetPagedAsync(null, "org_1", BookingStatus.Pending, 1, 10), Times.Once);
    }

    [Fact]
    public async Task AnOrganizer_MayNameOneOfTheirEvents() {
        await CreateService().GetManagedPagedAsync("org_1", UserRole.Organizer, "event_1", null, 1, 10);

        _bookingRepo.Verify(r => r.GetPagedAsync("event_1", "org_1", null, 1, 10), Times.Once);
    }

    [Theory]
    [InlineData("org_2", UserRole.Organizer, "event_1")] // somebody else's event
    [InlineData("org_1", UserRole.Organizer, "missing")]
    [InlineData("att_1", UserRole.Attendee, null)]
    public async Task Anyone_Else_IsRefused_AndNothingIsQueried(string userId, UserRole role, string? eventId) {
        _eventRepo.Setup(r => r.GetByIdAsync("missing")).ReturnsAsync((Event?)null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateService().GetManagedPagedAsync(userId, role, eventId, null, 1, 10));

        _bookingRepo.Verify(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<BookingStatus?>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }
}
