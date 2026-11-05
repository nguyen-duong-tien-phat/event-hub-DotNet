using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Payments;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

public class BookingFlowTests {
    private readonly Mock<ITicketRepository> _ticketRepo = new();
    private readonly Mock<IBookingRepository> _bookingRepo = new();
    private readonly Mock<IEventRepository> _eventRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPaymentService> _payment = new();

    public BookingFlowTests() {
        _ticketRepo.Setup(r => r.TryReserveAsync(It.IsAny<List<BookingTicket>>())).ReturnsAsync(true);
        _payment
            .Setup(p => p.CreatePaymentIntentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new PaymentIntentResult { PaymentIntentId = "pi_1", ClientSecret = "secret_1" });
    }

    private static readonly TicketCodeSigner Signer = new("test-secret-key-for-ticket-codes");

    private BookingService CreateService() =>
        new(_bookingRepo.Object, _ticketRepo.Object, _eventRepo.Object, _unitOfWork.Object, _payment.Object, Signer);

    private void GivenTicket(string id, decimal price, string eventId = "event_1", int maxPerOrder = 4, string type = "General") =>
        _ticketRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(new Ticket {
            Id = id, EventId = eventId, Type = type, Price = price,
            RemainingQuantity = 50, MaxPerOrder = maxPerOrder, Highlights = [],
        });

    private static CreateBookingRequest Request(string ticketId, int quantity, string eventId = "event_1") => new() {
        UserId = "user_1",
        EventId = eventId,
        Tickets = [new BookingTicketDto { TicketId = ticketId, Quantity = quantity }],
    };

    [Fact]
    public async Task CreateAsync_AddsOnePercentServiceFee_AndChargesTheTotal() {
        GivenTicket("t1", 25m);

        var result = await CreateService().CreateAsync(Request("t1", 2));

        Assert.NotNull(result);
        Assert.Equal(0.50m, result.Booking.ServiceFee);
        Assert.Equal(50.50m, result.Booking.TotalPrice);
        Assert.Equal("secret_1", result.ClientSecret);
        _payment.Verify(p => p.CreatePaymentIntentAsync(50.50m, "usd", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RoundsTheFeeToCents() {
        GivenTicket("t1", 18.50m);

        var result = await CreateService().CreateAsync(Request("t1", 1));

        Assert.NotNull(result);
        Assert.Equal(0.19m, result.Booking.ServiceFee); // 0.185 rounds up
        Assert.Equal(18.69m, result.Booking.TotalPrice);
    }

    [Fact]
    public async Task CreateAsync_RemembersTheTicketTypeName() {
        GivenTicket("t1", 90m, type: "VIP");

        var result = await CreateService().CreateAsync(Request("t1", 1));

        Assert.Equal("VIP", result!.Booking.Tickets.Single().TicketType);
    }

    [Fact]
    public async Task CreateAsync_FreeTickets_AreConfirmedWithoutPayment() {
        GivenTicket("t1", 0m);

        var result = await CreateService().CreateAsync(Request("t1", 2));

        Assert.NotNull(result);
        Assert.Equal(BookingStatus.Confirmed, result.Booking.Status);
        Assert.Equal(string.Empty, result.ClientSecret);
        _payment.Verify(p => p.CreatePaymentIntentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_TicketFromAnotherEvent_Throws() {
        GivenTicket("t1", 25m, eventId: "event_other");

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().CreateAsync(Request("t1", 1)));
        _bookingRepo.Verify(r => r.AddAsync(It.IsAny<Booking>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task CreateAsync_QuantityOutsideTheLimit_Throws(int quantity) {
        GivenTicket("t1", 25m, maxPerOrder: 4);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().CreateAsync(Request("t1", quantity)));
    }

    private static CreateBookingRequest KeyedRequest(string key) {
        var request = Request("t1", 1);
        request.IdempotencyKey = key;
        return request;
    }

    private static Booking ExistingBooking(string key, string? paymentIntentId = "pi_old", BookingStatus status = BookingStatus.Pending) => new() {
        EventId = "event_1", UserId = "user_1", IdempotencyKey = key, PaymentIntentId = paymentIntentId, Status = status,
    };

    [Fact]
    public async Task CreateAsync_NewKey_IsStoredOnTheBooking() {
        GivenTicket("t1", 25m);

        var result = await CreateService().CreateAsync(KeyedRequest("key-1"));

        Assert.False(result!.Replayed);
        Assert.Equal("key-1", result.Booking.IdempotencyKey);
    }

    [Fact]
    public async Task CreateAsync_WithoutKey_NeverLooksOneUp() {
        GivenTicket("t1", 25m);

        await CreateService().CreateAsync(Request("t1", 1));

        _bookingRepo.Verify(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_SeenKey_ReturnsTheOriginalBooking_WithoutHoldingOrChargingAgain() {
        GivenTicket("t1", 25m);
        var existing = ExistingBooking("key-1");
        _bookingRepo.Setup(r => r.GetByIdempotencyKeyAsync("user_1", "key-1")).ReturnsAsync(existing);
        _payment.Setup(p => p.GetClientSecretAsync("pi_old")).ReturnsAsync("secret_old");

        var result = await CreateService().CreateAsync(KeyedRequest("key-1"));

        Assert.True(result!.Replayed);
        Assert.Same(existing, result.Booking);
        Assert.Equal("secret_old", result.ClientSecret);
        _ticketRepo.Verify(r => r.TryReserveAsync(It.IsAny<List<BookingTicket>>()), Times.Never);
        _bookingRepo.Verify(r => r.AddAsync(It.IsAny<Booking>()), Times.Never);
        _payment.Verify(p => p.CreatePaymentIntentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_SeenKey_OnAConfirmedBooking_HasNoClientSecret() {
        _bookingRepo.Setup(r => r.GetByIdempotencyKeyAsync("user_1", "key-1"))
            .ReturnsAsync(ExistingBooking("key-1", status: BookingStatus.Confirmed));

        var result = await CreateService().CreateAsync(KeyedRequest("key-1"));

        Assert.Equal(string.Empty, result!.ClientSecret);
        _payment.Verify(p => p.GetClientSecretAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_SeenKey_WhilePaymentIsStillBeingCreated_AsksToRetry() {
        _bookingRepo.Setup(r => r.GetByIdempotencyKeyAsync("user_1", "key-1"))
            .ReturnsAsync(ExistingBooking("key-1", paymentIntentId: null));

        await Assert.ThrowsAsync<BookingInProgressException>(() => CreateService().CreateAsync(KeyedRequest("key-1")));
    }

    [Fact]
    public async Task CreateAsync_LosingTheRace_ReturnsTheWinnersBooking() {
        GivenTicket("t1", 25m);
        var winner = ExistingBooking("key-1");
        // Not there when we check first, there once our own insert fails on the unique index.
        _bookingRepo.SetupSequence(r => r.GetByIdempotencyKeyAsync("user_1", "key-1"))
            .ReturnsAsync((Booking?)null)
            .ReturnsAsync(winner);
        _bookingRepo.Setup(r => r.SaveChangesAsync()).ThrowsAsync(new InvalidOperationException("duplicate key"));
        _payment.Setup(p => p.GetClientSecretAsync("pi_old")).ReturnsAsync("secret_old");

        var result = await CreateService().CreateAsync(KeyedRequest("key-1"));

        Assert.True(result!.Replayed);
        Assert.Same(winner, result.Booking);
        _unitOfWork.Verify(u => u.RollbackAsync(), Times.Once);
        _bookingRepo.Verify(r => r.DetachPending(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_FailingForAnotherReason_StillThrows() {
        GivenTicket("t1", 25m);
        _bookingRepo.Setup(r => r.GetByIdempotencyKeyAsync("user_1", "key-1")).ReturnsAsync((Booking?)null);
        _bookingRepo.Setup(r => r.SaveChangesAsync()).ThrowsAsync(new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().CreateAsync(KeyedRequest("key-1")));
    }

    private Booking BookingWithIntent(BookingStatus status, string? paymentIntentId = "pi_1") {
        var booking = new Booking {
            EventId = "event_1", UserId = "user_1", Status = status, PaymentIntentId = paymentIntentId,
            Tickets = [new BookingTicket { TicketId = "t1", Quantity = 1, UnitPrice = 25m }],
        };
        _bookingRepo.Setup(r => r.GetByIdAsync(booking.Id)).ReturnsAsync(booking);
        if (paymentIntentId != null) {
            _bookingRepo.Setup(r => r.GetByPaymentIntentId(paymentIntentId)).ReturnsAsync(booking);
        }
        return booking;
    }

    private void VerifyRefunded(Booking booking, Func<Times> times) =>
        _payment.Verify(p => p.RefundAsync("pi_1", booking.Id), times);

    [Fact]
    public async Task CancelAsync_CancelsThePaymentIntent_AndDoesNotRefund() {
        var booking = BookingWithIntent(BookingStatus.Pending);
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_1")).ReturnsAsync(true);

        var result = await CreateService().CancelAsync(booking.Id, "user_1", UserRole.Attendee);

        Assert.Equal(BookingStatus.Cancelled, result!.Status);
        _payment.Verify(p => p.CancelPaymentIntentAsync("pi_1"), Times.Once);
        VerifyRefunded(booking, Times.Never);
    }

    [Fact]
    public async Task CancelAsync_WhenTheIntentWasAlreadyPaid_RefundsOnce() {
        var booking = BookingWithIntent(BookingStatus.Pending);
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_1")).ReturnsAsync(false);
        _payment.Setup(p => p.IsPaymentSucceededAsync("pi_1")).ReturnsAsync(true);

        await CreateService().CancelAsync(booking.Id, "user_1", UserRole.Attendee);

        VerifyRefunded(booking, Times.Once);
    }

    [Fact]
    public async Task CancelAsync_WhenTheIntentCannotBeCancelledButWasNotPaid_DoesNotRefund() {
        var booking = BookingWithIntent(BookingStatus.Pending);
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_1")).ReturnsAsync(false);
        _payment.Setup(p => p.IsPaymentSucceededAsync("pi_1")).ReturnsAsync(false);

        await CreateService().CancelAsync(booking.Id, "user_1", UserRole.Attendee);

        VerifyRefunded(booking, Times.Never);
    }

    [Fact]
    public async Task CancelAsync_FreeBooking_NeverTouchesStripe() {
        var booking = BookingWithIntent(BookingStatus.Confirmed, paymentIntentId: null);

        await CreateService().CancelAsync(booking.Id, "user_1", UserRole.Attendee);

        _payment.Verify(p => p.CancelPaymentIntentAsync(It.IsAny<string>()), Times.Never);
        _payment.Verify(p => p.RefundAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_OnACancelledBookingThatWasPaid_Refunds() {
        var booking = BookingWithIntent(BookingStatus.Cancelled);
        _payment.Setup(p => p.IsPaymentSucceededAsync("pi_1")).ReturnsAsync(true);

        var result = await CreateService().ConfirmPaymentAsync(booking.Id, "user_1");

        Assert.Equal(BookingStatus.Cancelled, result!.Status); // the tickets are gone: it stays cancelled
        VerifyRefunded(booking, Times.Once);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_OnACancelledBookingThatWasNotPaid_DoesNotRefund() {
        var booking = BookingWithIntent(BookingStatus.Cancelled);
        _payment.Setup(p => p.IsPaymentSucceededAsync("pi_1")).ReturnsAsync(false);

        await CreateService().ConfirmPaymentAsync(booking.Id, "user_1");

        VerifyRefunded(booking, Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_OnAPaidPendingBooking_ConfirmsWithoutRefund() {
        var booking = BookingWithIntent(BookingStatus.Pending);
        _payment.Setup(p => p.IsPaymentSucceededAsync("pi_1")).ReturnsAsync(true);

        var result = await CreateService().ConfirmPaymentAsync(booking.Id, "user_1");

        Assert.Equal(BookingStatus.Confirmed, result!.Status);
        VerifyRefunded(booking, Times.Never);
    }

    [Fact]
    public async Task HandlePaymentSucceeded_OnACancelledBookingThatWasPaid_Refunds() {
        var booking = BookingWithIntent(BookingStatus.Cancelled);
        _payment.Setup(p => p.IsPaymentSucceededAsync("pi_1")).ReturnsAsync(true);

        await CreateService().HandlePaymentSucceeded("pi_1");

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        VerifyRefunded(booking, Times.Once);
    }

    [Fact]
    public async Task HandlePaymentSucceeded_OnAPendingBooking_ConfirmsWithoutRefund() {
        var booking = BookingWithIntent(BookingStatus.Pending);

        await CreateService().HandlePaymentSucceeded("pi_1");

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        VerifyRefunded(booking, Times.Never);
    }

    [Fact]
    public async Task HandlePaymentSucceeded_OnAnAlreadyConfirmedBooking_DoesNothing() {
        var booking = BookingWithIntent(BookingStatus.Confirmed);

        await CreateService().HandlePaymentSucceeded("pi_1");

        VerifyRefunded(booking, Times.Never);
        _bookingRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task ExpireAbandonedBookingAsync_StopsThePaymentOfEveryExpiredBooking() {
        var first = BookingWithIntent(BookingStatus.Pending, "pi_a");
        var second = BookingWithIntent(BookingStatus.Pending, "pi_b");
        _bookingRepo
            .Setup(r => r.GetExpiredPendingBookingsAsync(It.IsAny<DateTime>()))
            .ReturnsAsync([first, second]);
        _payment.Setup(p => p.CancelPaymentIntentAsync(It.IsAny<string>())).ReturnsAsync(true);

        await CreateService().ExpireAbandonedBookingAsync(BookingService.HoldDuration);

        Assert.Equal(BookingStatus.Cancelled, first.Status);
        Assert.Equal(BookingStatus.Cancelled, second.Status);
        _payment.Verify(p => p.CancelPaymentIntentAsync("pi_a"), Times.Once);
        _payment.Verify(p => p.CancelPaymentIntentAsync("pi_b"), Times.Once);
    }

    [Fact]
    public async Task ExpireAbandonedBookingAsync_WhenCancellingFails_LeavesItsPaymentAlone_AndCarriesOn() {
        var broken = BookingWithIntent(BookingStatus.Pending, "pi_a");
        var healthy = BookingWithIntent(BookingStatus.Pending, "pi_b");
        _bookingRepo
            .Setup(r => r.GetExpiredPendingBookingsAsync(It.IsAny<DateTime>()))
            .ReturnsAsync([broken, healthy]);
        _ticketRepo.Setup(r => r.ReleaseAsync(broken.Tickets)).ThrowsAsync(new InvalidOperationException("db down"));
        _payment.Setup(p => p.CancelPaymentIntentAsync(It.IsAny<string>())).ReturnsAsync(true);

        await CreateService().ExpireAbandonedBookingAsync(BookingService.HoldDuration);

        // Its booking is still held, so a customer paying right now must not be refused.
        _payment.Verify(p => p.CancelPaymentIntentAsync("pi_a"), Times.Never);
        _unitOfWork.Verify(u => u.RollbackAsync(), Times.Once);
        // The next booking is not skipped.
        _payment.Verify(p => p.CancelPaymentIntentAsync("pi_b"), Times.Once);
    }

    [Fact]
    public async Task ExpireAbandonedBookingAsync_WhenStripeFails_StillCancelsAndCarriesOn() {
        var first = BookingWithIntent(BookingStatus.Pending, "pi_a");
        var second = BookingWithIntent(BookingStatus.Pending, "pi_b");
        _bookingRepo
            .Setup(r => r.GetExpiredPendingBookingsAsync(It.IsAny<DateTime>()))
            .ReturnsAsync([first, second]);
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_a")).ThrowsAsync(new InvalidOperationException("stripe down"));
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_b")).ReturnsAsync(true);

        await CreateService().ExpireAbandonedBookingAsync(BookingService.HoldDuration);

        Assert.Equal(BookingStatus.Cancelled, first.Status);
        Assert.Equal(BookingStatus.Cancelled, second.Status);
        _payment.Verify(p => p.CancelPaymentIntentAsync("pi_b"), Times.Once);
        _unitOfWork.Verify(u => u.RollbackAsync(), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_WhenStripeFails_DoesNotRollBackTheCommittedCancellation() {
        var booking = BookingWithIntent(BookingStatus.Pending);
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_1")).ThrowsAsync(new InvalidOperationException("stripe down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().CancelAsync(booking.Id, "user_1", UserRole.Attendee));

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        _unitOfWork.Verify(u => u.CommitAsync(), Times.Once);
        _unitOfWork.Verify(u => u.RollbackAsync(), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_CalledTwice_ReturnsTheCancelledBooking_AndGivesTicketsBackOnce() {
        var booking = BookingWithIntent(BookingStatus.Pending);
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_1")).ReturnsAsync(true);
        var service = CreateService();

        await service.CancelAsync(booking.Id, "user_1", UserRole.Attendee);
        var second = await service.CancelAsync(booking.Id, "user_1", UserRole.Attendee);

        Assert.Equal(BookingStatus.Cancelled, second!.Status);
        _ticketRepo.Verify(r => r.ReleaseAsync(booking.Tickets), Times.Once);
        _payment.Verify(p => p.CancelPaymentIntentAsync("pi_1"), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_OnAnAlreadyCancelledBooking_DoesNotTouchTicketsOrStripe() {
        var booking = BookingWithIntent(BookingStatus.Cancelled);

        var result = await CreateService().CancelAsync(booking.Id, "user_1", UserRole.Attendee);

        Assert.Equal(BookingStatus.Cancelled, result!.Status);
        _ticketRepo.Verify(r => r.ReleaseAsync(It.IsAny<List<BookingTicket>>()), Times.Never);
        _payment.Verify(p => p.CancelPaymentIntentAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_OnSomeoneElsesCancelledBooking_StillThrows() {
        var booking = BookingWithIntent(BookingStatus.Cancelled);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService().CancelAsync(booking.Id, "user_2", UserRole.Attendee));
    }

    private void GivenEventOf(string organizerId) =>
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(new Event {
            Id = "event_1", Title = "t", Description = "d", Location = "l", OrganizerId = organizerId,
            ImageUrl = "x", Highlights = [], StartsAt = DateTime.UtcNow.AddDays(10)
        });

    [Fact]
    public async Task CancelAsync_AnAdminCancellingSomeoneElsesPaidBooking_CancelsAndRefunds() {
        var booking = BookingWithIntent(BookingStatus.Confirmed);
        GivenEventOf("org_1");
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_1")).ReturnsAsync(false);
        _payment.Setup(p => p.IsPaymentSucceededAsync("pi_1")).ReturnsAsync(true);

        var result = await CreateService().CancelAsync(booking.Id, "admin_1", UserRole.Admin);

        Assert.Equal(BookingStatus.Cancelled, result!.Status);
        _ticketRepo.Verify(r => r.ReleaseAsync(It.IsAny<List<BookingTicket>>()), Times.Once);
        VerifyRefunded(booking, Times.Once);
    }

    [Fact]
    public async Task CancelAsync_TheOrganizerOfTheEvent_MayCancelItsBookings() {
        var booking = BookingWithIntent(BookingStatus.Pending);
        GivenEventOf("org_1");
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_1")).ReturnsAsync(true);

        var result = await CreateService().CancelAsync(booking.Id, "org_1", UserRole.Organizer);

        Assert.Equal(BookingStatus.Cancelled, result!.Status);
    }

    [Fact]
    public async Task CancelAsync_AnOrganizerOfAnotherEvent_IsRefused() {
        var booking = BookingWithIntent(BookingStatus.Pending);
        GivenEventOf("org_1");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService().CancelAsync(booking.Id, "org_2", UserRole.Organizer));

        _ticketRepo.Verify(r => r.ReleaseAsync(It.IsAny<List<BookingTicket>>()), Times.Never);
    }

    [Fact]
    public async Task ToResponseAsync_ShowsSubtotalFeeTotalAndHoldTime() {
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(new Event {
            Id = "event_1", Title = "Neon Nights", Description = "d", Location = "Hà Nội", OrganizerId = "o",
            ImageUrl = "u", Highlights = [], StartsAt = new DateTime(2030, 1, 1, 19, 0, 0, DateTimeKind.Utc),
        });
        var booking = new Booking {
            EventId = "event_1", UserId = "user_1", TotalPrice = 50.50m, ServiceFee = 0.50m,
            Tickets = [new BookingTicket { TicketId = "t1", TicketType = "General", Quantity = 2, UnitPrice = 25m }],
        };

        var dto = await CreateService().ToResponseAsync(booking, "secret_1");

        Assert.Equal(50.00m, dto.Subtotal);
        Assert.Equal(0.50m, dto.ServiceFee);
        Assert.Equal(50.50m, dto.TotalPrice);
        Assert.Equal(2, dto.Quantity);
        Assert.Equal("Neon Nights", dto.Event!.Title);
        Assert.Equal("General", dto.Tickets.Single().Type);
        Assert.Equal(booking.CreatedAt + BookingService.HoldDuration, dto.ExpiresAt);
        Assert.Equal("secret_1", dto.ClientSecret);
    }

    [Fact]
    public async Task ToResponseAsync_WithoutClientSecret_LeavesItOut() {
        var booking = new Booking { EventId = "event_1", UserId = "user_1" };

        var dto = await CreateService().ToResponseAsync(booking);

        Assert.Null(dto.ClientSecret);
    }

    [Fact]
    public async Task ToResponseAsync_ConfirmedBooking_HasOneSignedCodePerTicket() {
        var booking = new Booking {
            EventId = "event_1", UserId = "user_1", Status = BookingStatus.Confirmed,
            Tickets = [
                new BookingTicket { TicketId = "t1", Quantity = 2 },
                new BookingTicket { TicketId = "t2", Quantity = 1 },
            ],
        };

        var dto = await CreateService().ToResponseAsync(booking);

        Assert.Equal(3, dto.TicketCodes.Count);
        for (var i = 0; i < 3; i++) {
            Assert.True(Signer.TryVerify(dto.TicketCodes[i], out var id, out var number));
            Assert.Equal(booking.Id, id);
            Assert.Equal(i + 1, number);
        }
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task ToResponseAsync_UnpaidOrCancelledBooking_HasNoCodes(BookingStatus status) {
        var booking = new Booking {
            EventId = "event_1", UserId = "user_1", Status = status,
            Tickets = [new BookingTicket { TicketId = "t1", Quantity = 2 }],
        };

        var dto = await CreateService().ToResponseAsync(booking);

        Assert.Empty(dto.TicketCodes);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_WhenStripeSaysPaid_ConfirmsTheBooking() {
        var booking = new Booking { EventId = "e", UserId = "user_1", PaymentIntentId = "pi_1" };
        _bookingRepo.Setup(r => r.GetByIdAsync(booking.Id)).ReturnsAsync(booking);
        _payment.Setup(p => p.IsPaymentSucceededAsync("pi_1")).ReturnsAsync(true);

        var result = await CreateService().ConfirmPaymentAsync(booking.Id, "user_1");

        Assert.Equal(BookingStatus.Confirmed, result!.Status);
        _bookingRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_WhenNotPaidYet_StaysPending() {
        var booking = new Booking { EventId = "e", UserId = "user_1", PaymentIntentId = "pi_1" };
        _bookingRepo.Setup(r => r.GetByIdAsync(booking.Id)).ReturnsAsync(booking);
        _payment.Setup(p => p.IsPaymentSucceededAsync("pi_1")).ReturnsAsync(false);

        var result = await CreateService().ConfirmPaymentAsync(booking.Id, "user_1");

        Assert.Equal(BookingStatus.Pending, result!.Status);
        _bookingRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_ForSomeoneElsesBooking_Throws() {
        var booking = new Booking { EventId = "e", UserId = "user_1", PaymentIntentId = "pi_1" };
        _bookingRepo.Setup(r => r.GetByIdAsync(booking.Id)).ReturnsAsync(booking);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService().ConfirmPaymentAsync(booking.Id, "user_2"));
        _payment.Verify(p => p.IsPaymentSucceededAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ToResponsesAsync_LoadsEachEventOnce_AndNeverInParallel() {
        var running = 0;
        var overlapped = false;
        _eventRepo.Setup(r => r.GetByIdAsync(It.IsAny<string>())).Returns(async (string id) => {
            if (Interlocked.Increment(ref running) > 1) overlapped = true;
            await Task.Delay(20);
            Interlocked.Decrement(ref running);
            return new Event {
                Id = id, Title = $"Event {id}", Description = "d", Location = "L", OrganizerId = "o",
                ImageUrl = "u", Highlights = [], StartsAt = DateTime.UtcNow,
            };
        });
        var bookings = new[] {
            new Booking { EventId = "event_a", UserId = "u" },
            new Booking { EventId = "event_b", UserId = "u" },
            new Booking { EventId = "event_a", UserId = "u" },
            new Booking { EventId = "event_c", UserId = "u" },
        };

        var responses = await CreateService().ToResponsesAsync(bookings);

        Assert.Equal(4, responses.Count);
        Assert.Equal(["Event event_a", "Event event_b", "Event event_a", "Event event_c"], responses.Select(r => r.Event!.Title));
        Assert.False(overlapped, "two queries ran on the shared DbContext at the same time");
        _eventRepo.Verify(r => r.GetByIdAsync("event_a"), Times.Once);
    }
}
