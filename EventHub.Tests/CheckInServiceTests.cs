using EventHub.Core.Bookings;
using EventHub.Core.Events;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

public class CheckInServiceTests {
    private readonly TicketCodeSigner _signer = new("a-secret-key-long-enough-for-hmac");
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IEventRepository> _events = new();
    private readonly Mock<IAdmissionRepository> _admissions = new();
    private readonly Booking _booking;
    private readonly Admission _admission;

    public CheckInServiceTests() {
        _booking = new Booking {
            EventId = "event_1", UserId = "buyer", Status = BookingStatus.Confirmed,
            Tickets = [new BookingTicket { TicketId = "t2", TicketType = "VIP", Quantity = 1 }],
        };
        _admission = new Admission { BookingId = _booking.Id, TicketId = "t2", TicketType = "VIP", Code = "K7Q49XMD" };
        _bookings.Setup(r => r.GetByIdAsync(_booking.Id)).ReturnsAsync(_booking);
        _events.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(new Event {
            Id = "event_1", Title = "Neon Nights", Description = "d", Location = "l", OrganizerId = "organizer_1",
            ImageUrl = "u", Highlights = [], StartsAt = DateTime.UtcNow.AddDays(1),
        });
        _admissions.Setup(r => r.FindByIdAsync(_admission.Id)).ReturnsAsync(_admission);
        _admissions.Setup(r => r.FindByCodeAsync("K7Q49XMD")).ReturnsAsync(_admission);
        _admissions.Setup(r => r.TryCheckInAsync(_admission.Id, It.IsAny<string>())).ReturnsAsync(true);
    }

    private CheckInService Service() => new(_signer, _bookings.Object, _events.Object, _admissions.Object);

    private string Qr => _signer.Sign(_admission.Id);

    [Fact]
    public async Task ValidQr_IsAdmitted_WithItsTypeFromTheAdmission() {
        var result = await Service().CheckInAsync(Qr, "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.Admitted, result.Status);
        Assert.Equal("VIP", result.TicketType);
        Assert.Equal("Neon Nights", result.EventTitle);
        Assert.Equal(_admission.Id, result.AdmissionId);
        Assert.Equal("K7Q49XMD", result.ShortCode);
        _admissions.Verify(r => r.TryCheckInAsync(_admission.Id, "organizer_1"), Times.Once);
    }

    [Theory]
    [InlineData("K7Q49XMD")]
    [InlineData("k7q4-9xmd")] // typed by hand: any case, with a dash
    [InlineData(" K7Q4 9XMD ")]
    public async Task ShortCode_TypedByHand_IsAdmitted(string typed) {
        var result = await Service().CheckInAsync(typed, "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.Admitted, result.Status);
        _admissions.Verify(r => r.TryCheckInAsync(_admission.Id, "organizer_1"), Times.Once);
    }

    [Fact]
    public async Task SecondScan_IsAlreadyUsed_AndSaysWhenItFirstGotIn() {
        var firstScan = new DateTime(2030, 1, 1, 18, 0, 0, DateTimeKind.Utc);
        _admissions.Setup(r => r.TryCheckInAsync(_admission.Id, It.IsAny<string>())).ReturnsAsync(false);
        _admissions.SetupSequence(r => r.FindByIdAsync(_admission.Id))
            .ReturnsAsync(_admission)
            .ReturnsAsync(new Admission { BookingId = _booking.Id, TicketId = "t2", CheckedInAt = firstScan });

        var result = await Service().CheckInAsync(Qr, "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.AlreadyUsed, result.Status);
        Assert.Equal(firstScan, result.CheckedInAt);
    }

    [Theory]
    [InlineData("eh2.adm_x.badsignature")]
    [InlineData("eh1.booking_x.1.badsignature")] // the old numbered format is gone
    [InlineData("NOTACODE")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ForgedOrUnknownCode_IsInvalid_AndTouchesNothing(string? code) {
        var result = await Service().CheckInAsync(code, "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.Invalid, result.Status);
        _admissions.Verify(r => r.TryCheckInAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SignedCodeForAMissingAdmission_IsInvalid() {
        var result = await Service().CheckInAsync(_signer.Sign("adm_gone"), "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task ShortCodeThatMatchesNothing_IsInvalid() {
        var result = await Service().CheckInAsync("ZZZZZZZZ", "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.Invalid, result.Status);
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task UnpaidOrCancelledBooking_IsNotAdmitted(BookingStatus status) {
        _booking.Status = status;

        var result = await Service().CheckInAsync(Qr, "organizer_1", UserRole.Organizer);

        Assert.Equal(CheckInStatus.NotPaid, result.Status);
        _admissions.Verify(r => r.TryCheckInAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnotherOrganizer_CannotAdmitPeopleToThisEvent() {
        var result = await Service().CheckInAsync(Qr, "organizer_2", UserRole.Organizer);

        Assert.Equal(CheckInStatus.Forbidden, result.Status);
        _admissions.Verify(r => r.TryCheckInAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Admin_CanAdmitPeopleToAnyEvent() {
        var result = await Service().CheckInAsync(Qr, "some_admin", UserRole.Admin);

        Assert.Equal(CheckInStatus.Admitted, result.Status);
    }
}
