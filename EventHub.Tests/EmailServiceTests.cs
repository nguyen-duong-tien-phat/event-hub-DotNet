using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Email;
using EventHub.Core.Events;
using EventHub.Core.Payments;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

public class EmailServiceTests {
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IEventRepository> _events = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IAdmissionRepository> _admissions = new();
    private readonly Booking _booking;
    private readonly Admission _admission;
    private readonly Event _event = new() {
        Id = "event_1", Title = "Neon Nights", Description = "d", Location = "Hall 1", OrganizerId = "organizer_1",
        ImageUrl = "u", Highlights = [], StartsAt = new DateTime(2027, 1, 2, 19, 30, 0, DateTimeKind.Utc),
    };

    public EmailServiceTests() {
        _booking = new Booking {
            EventId = "event_1", UserId = "buyer", Status = BookingStatus.Pending, PaymentIntentId = "pi_1", TotalPrice = 50.5m,
            Tickets = [new BookingTicket { TicketId = "t1", TicketType = "VIP", Quantity = 2, UnitPrice = 25m }],
        };
        _admission = new Admission { BookingId = _booking.Id, TicketId = "t1", TicketType = "VIP", Code = "K7Q49XMD" };

        _users.Setup(r => r.GetByIdAsync("buyer")).ReturnsAsync(new User { Id = "buyer", Email = "a@b.c", FullName = "Ann", PasswordHash = "x" });
        _events.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(_event);
        _bookings.Setup(r => r.GetByIdAsync(_booking.Id)).ReturnsAsync(_booking);
        _bookings.Setup(r => r.GetByPaymentIntentId("pi_1")).ReturnsAsync(_booking);
    }

    private EmailService Emails() => new(_users.Object, _events.Object, _email.Object);

    private void VerifySent(string key, Func<Times> times) =>
        _email.Verify(e => e.SendAsync("a@b.c", It.IsAny<string>(), It.IsAny<string>(), key), times);

    [Fact]
    public async Task BookingConfirmed_EmailsTheBuyer_WithOneKeyPerBooking() {
        string? subject = null, body = null;
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((_, s, b, _) => { subject = s; body = b; })
            .Returns(Task.CompletedTask);

        await Emails().BookingConfirmedAsync(_booking);

        VerifySent($"booking-confirmed:{_booking.Id}", Times.Once);
        Assert.Contains("Neon Nights", subject);
        Assert.Contains("2 x VIP", body);
        Assert.Contains("Hall 1", body);
        Assert.Contains("50.50", body);
    }

    [Fact]
    public async Task BookingConfirmed_WhenTheQueueIsDown_DoesNotFailTheBooking() {
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("queue down"));

        await Emails().BookingConfirmedAsync(_booking);
    }

    [Fact]
    public async Task BookingConfirmed_ForAnUnknownBuyer_SendsNothing() {
        _users.Setup(r => r.GetByIdAsync("buyer")).ReturnsAsync((User?)null);

        await Emails().BookingConfirmedAsync(_booking);

        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckedIn_EmailsTheBuyer_WithOneKeyPerAdmission() {
        await Emails().CheckedInAsync(_booking, _admission, _event);

        VerifySent($"checked-in:{_admission.Id}", Times.Once);
    }

    [Fact]
    public async Task HandlePaymentSucceeded_SendsTheConfirmationEmail() {
        var service = new BookingService(_bookings.Object, new Mock<ITicketRepository>().Object, _events.Object,
            new Mock<IUnitOfWork>().Object, new Mock<IPaymentService>().Object, null, _admissions.Object, Emails());

        await service.HandlePaymentSucceeded("pi_1");

        VerifySent($"booking-confirmed:{_booking.Id}", Times.Once);
    }

    [Fact]
    public async Task HandlePaymentSucceeded_OnAnAlreadyConfirmedBooking_SendsNothing() {
        _booking.Status = BookingStatus.Confirmed;
        var service = new BookingService(_bookings.Object, new Mock<ITicketRepository>().Object, _events.Object,
            new Mock<IUnitOfWork>().Object, new Mock<IPaymentService>().Object, null, _admissions.Object, Emails());

        await service.HandlePaymentSucceeded("pi_1");

        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private CheckInService CheckIn() {
        _booking.Status = BookingStatus.Confirmed;
        _admissions.Setup(r => r.FindByCodeAsync("K7Q49XMD")).ReturnsAsync(_admission);
        return new CheckInService(new TicketCodeSigner("a-secret-key-long-enough-for-hmac"), _bookings.Object,
            _events.Object, _admissions.Object, Emails());
    }

    [Fact]
    public async Task CheckIn_WhenAdmitted_SendsTheEmail() {
        _admissions.Setup(r => r.TryCheckInAsync(_admission.Id, "organizer_1")).ReturnsAsync(true);

        await CheckIn().CheckInAsync("K7Q49XMD", "organizer_1", UserRole.Organizer);

        VerifySent($"checked-in:{_admission.Id}", Times.Once);
    }

    [Fact]
    public async Task CheckIn_WhenAlreadyUsed_SendsNothing() {
        _admissions.Setup(r => r.TryCheckInAsync(_admission.Id, "organizer_1")).ReturnsAsync(false);
        _admissions.Setup(r => r.FindByIdAsync(_admission.Id)).ReturnsAsync(_admission);

        await CheckIn().CheckInAsync("K7Q49XMD", "organizer_1", UserRole.Organizer);

        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
