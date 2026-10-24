using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Payments;
using EventHub.Core.Tickets;
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

    private BookingService CreateService() =>
        new(_bookingRepo.Object, _ticketRepo.Object, _eventRepo.Object, _unitOfWork.Object, _payment.Object);

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
