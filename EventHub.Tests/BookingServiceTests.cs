using EventHub.Core.Entities;
using EventHub.Core.Enums;
using EventHub.Core.Interfaces;
using EventHub.Core.Services;
using EventHub.Core.Services.Models;
using EventHub.Infrastructure.Payments;
using Moq;

namespace EventHub.Tests;

public class BookingServiceTests {
    [Fact]
    public async Task CreateASync_WhenTicketAvailable_CreatesBookingSuccessfully() {
        var ticketRepo = new Mock<ITicketRepository>();
        var bookingRepo = new Mock<IBookingRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        ticketRepo
            .Setup(r => r.TryReserveAsync(It.IsAny<string>(),  It.IsAny<int>()))
            .ReturnsAsync(true);
        ticketRepo.Setup(r => r.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new Ticket { Id = string.Empty, EventId = string.Empty, RemainingQuantity = 10, MaxPerOrder = 2, Highlights = []});
        
        var paymentService = new Mock<IPaymentService>();
        paymentService
            .Setup(r => r.CreatePaymentIntentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new PaymentIntentResult {
                PaymentIntentId = "paymentIntentId",
                ClientSecret = "clientSecret",
            });

        var service = new BookingService(bookingRepo.Object, ticketRepo.Object, unitOfWork.Object, paymentService.Object);

        var request = new CreateBookingRequest {
            UserId = string.Empty,
            TicketId = string.Empty,
            Quantity = 1
        };
        
        // Act 
        var result = await service.CreateAsync(request);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(BookingStatus.Pending, result.Booking.Status);
        bookingRepo.Verify(r => r.AddAsync(It.IsAny<Booking>()), Times.Once);
        unitOfWork.Verify(u => u.CommitAsync(), Times.Once);
    }
    
    [Fact]
    public async Task CreateAsync_WhenNotEnoughTickets_ReturnsNullAndRollsBack() {
        var ticketRepo = new Mock<ITicketRepository>();
        var bookingRepo = new Mock<IBookingRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        
        ticketRepo
            .Setup(r => r.TryReserveAsync(It.IsAny<string>(),  It.IsAny<int>()))
            .ReturnsAsync(false); // sold out
        ticketRepo.Setup(r => r.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new Ticket { Id = string.Empty, EventId = string.Empty, RemainingQuantity = 10, MaxPerOrder = 2, Highlights = []});

        
        var service = new BookingService(bookingRepo.Object, ticketRepo.Object, unitOfWork.Object, new StripePaymentService());

        var request = new CreateBookingRequest {
            UserId = string.Empty,
            TicketId = string.Empty,
            Quantity = 1
        };
        
        // Act 
        var result = await service.CreateAsync(request);
        
        // Assert
        Assert.Null(result);
        bookingRepo.Verify(r => r.AddAsync(It.IsAny<Booking>()), Times.Never);
        unitOfWork.Verify(u => u.RollbackAsync(), Times.Once);
        unitOfWork.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateASync_WhenTicketDoesNotExist_ThrowsKeyNotFoundException() {
        var ticketRepo = new Mock<ITicketRepository>();
        var bookingRepo = new Mock<IBookingRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        ticketRepo.Setup(r => r.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Ticket?)null);

        var service = new BookingService(bookingRepo.Object, ticketRepo.Object, unitOfWork.Object, new StripePaymentService());

        var request = new CreateBookingRequest {
            UserId = string.Empty,
            TicketId = string.Empty,
            Quantity = 1
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(request));
    }
}