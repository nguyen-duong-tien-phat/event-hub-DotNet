using EventHub.Core.Bookings;
using EventHub.Core.Events;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

public class CheckInServiceTests {
    private readonly TicketCodeSigner _signer = new("a-secret-key-long-enough-for-hmac");
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IEventRepository> _events = new();
    private readonly Mock<ICheckInRepository> _checkIns = new();
    private readonly Booking _booking;

    public CheckInServiceTests() {
        // "2 × General, 1 × VIP": tickets 1 and 2 are General, 3 is VIP.
        _booking = new Booking {
            EventId = "event_1", UserId = "buyer", Status = BookingStatus.Confirmed,
            Tickets = [
                new BookingTicket { TicketId = "t1", TicketType = "General", Quantity = 2 },
                new BookingTicket { TicketId = "t2", TicketType = "VIP", Quantity = 1 },
            ],
        };
        _bookings.Setup(r => r.GetByIdAsync(_booking.Id)).ReturnsAsync(_booking);
        _events.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(new Event {
            Id = "event_1", Title = "Neon Nights", Description = "d", Location = "l", OrganizerId = "organizer_1",
            ImageUrl = "u", Highlights = [], StartsAt = DateTime.UtcNow.AddDays(1),
        });
        _checkIns.Setup(r => r.TryAddAsync(It.IsAny<TicketCheckIn>())).ReturnsAsync(true);
    }

    private CheckInService Service() => new(_signer, _bookings.Object, _events.Object, _checkIns.Object);

    private string Code(int number) => _signer.Sign(_booking.Id, number);

    [Fact]
    public async Task ValidTicket_IsAdmitted_WithItsTypeFromTheBooking() {
        var result = await Service().CheckInAsync(Code(3), "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.Admitted, result.Status);
        Assert.Equal("VIP", result.TicketType);
        Assert.Equal("Neon Nights", result.EventTitle);
        _checkIns.Verify(r => r.TryAddAsync(It.Is<TicketCheckIn>(c =>
            c.BookingId == _booking.Id && c.Number == 3 && c.CheckedInBy == "organizer_1")), Times.Once);
    }

    [Fact]
    public async Task SecondScan_IsAlreadyUsed_AndSaysWhenItFirstGotIn() {
        var firstScan = new DateTime(2030, 1, 1, 18, 0, 0, DateTimeKind.Utc);
        _checkIns.Setup(r => r.TryAddAsync(It.IsAny<TicketCheckIn>())).ReturnsAsync(false);
        _checkIns.Setup(r => r.GetAsync(_booking.Id, 1))
            .ReturnsAsync(new TicketCheckIn { BookingId = _booking.Id, Number = 1, CheckedInBy = "x", CreatedAt = firstScan });

        var result = await Service().CheckInAsync(Code(1), "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.AlreadyUsed, result.Status);
        Assert.Equal(firstScan, result.CheckedInAt);
    }

    [Theory]
    [InlineData("eventhub:ticket:booking_x:1")]
    [InlineData("eh1.booking_x.1.badsignature")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ForgedOrUnsignedCode_IsInvalid_AndTouchesNothing(string? code) {
        var result = await Service().CheckInAsync(code, "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.Invalid, result.Status);
        _checkIns.Verify(r => r.TryAddAsync(It.IsAny<TicketCheckIn>()), Times.Never);
    }

    [Fact]
    public async Task SignedCodeForANumberPastTheLastTicket_IsInvalid() {
        var result = await Service().CheckInAsync(Code(4), "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task SignedCodeForAMissingBooking_IsInvalid() {
        var result = await Service().CheckInAsync(_signer.Sign("booking_gone", 1), "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.Invalid, result.Status);
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task UnpaidOrCancelledBooking_IsNotAdmitted(BookingStatus status) {
        _booking.Status = status;

        var result = await Service().CheckInAsync(Code(1), "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.NotPaid, result.Status);
        _checkIns.Verify(r => r.TryAddAsync(It.IsAny<TicketCheckIn>()), Times.Never);
    }

    [Fact]
    public async Task AnotherOrganizer_CannotAdmitPeopleToThisEvent() {
        var result = await Service().CheckInAsync(Code(1), "organizer_2", UserRole.Organizer);

        Assert.Equal(CheckInStatus.Forbidden, result.Status);
        _checkIns.Verify(r => r.TryAddAsync(It.IsAny<TicketCheckIn>()), Times.Never);
    }

    [Fact]
    public async Task Admin_CanAdmitPeopleToAnyEvent() {
        var result = await Service().CheckInAsync(Code(1), "some_admin", UserRole.Admin);

        Assert.Equal(CheckInStatus.Admitted, result.Status);
    }
}
