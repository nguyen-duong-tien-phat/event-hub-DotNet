using EventHub.Core.Common;
using EventHub.Core.Entities;
using EventHub.Core.Enums;
using EventHub.Core.Interfaces;
using EventHub.Core.Services.Models;

namespace EventHub.Core.Services;

public class BookingService (
    IBookingRepository bookingRepository, 
    ITicketRepository ticketRepository,
    IUnitOfWork unitOfWork,
    IPaymentService paymentService)
{
    public async Task<BookingWithPaymentResult?> CreateAsync(CreateBookingRequest request) {
        var ticket = await ticketRepository.GetByIdAsync(request.TicketId);
        if (ticket == null) {
            throw new KeyNotFoundException("Ticket not found");
        }

        Booking booking;
        
        await unitOfWork.BeginTransactionAsync();
        try {
            var reserved = await ticketRepository.TryReserveAsync(request.TicketId, request.Quantity);
            if (!reserved) { // sold out, or not enough remaining
                await unitOfWork.RollbackAsync();
                return null;
            } 

            booking = new Booking {
                UserId = request.UserId,
                TicketId = request.TicketId,
                Quantity = request.Quantity,
                Status = BookingStatus.Pending,
                UnitPrice = ticket.Price,
                TotalPrice = ticket.Price * request.Quantity
            };
            
            await bookingRepository.AddAsync(booking);
            await bookingRepository.SaveChangesAsync();

            await unitOfWork.CommitAsync();
        }
        catch {
            await unitOfWork.RollbackAsync();
            throw;
        }
        

        try {
            var totalAmount = ticket.Price * request.Quantity;
            var paymentIntent = await paymentService.CreatePaymentIntentAsync(totalAmount, "usd", booking.Id);

            booking.PaymentIntentId = paymentIntent.PaymentIntentId;
            bookingRepository.Update(booking);
            await bookingRepository.SaveChangesAsync();

            return new BookingWithPaymentResult {
                Booking = booking,
                ClientSecret = paymentIntent.ClientSecret
            };
        }
        catch (Exception) {
            booking.Status = BookingStatus.Cancelled;
            bookingRepository.Update(booking);
            await ticketRepository.ReleaseAsync(booking.TicketId, booking.Quantity);
            await bookingRepository.SaveChangesAsync();
            throw new InvalidOperationException("Failed to initialize payment. Your reservation has been released.");
        }
    }

    public Task<List<Booking>> GetByUserIdAsync(Guid userId) => bookingRepository.GetByUserIdAsync(userId);
    
    public async Task<PagedResult<Booking>> GetPagedByUserIdAsync(Guid userId, int page, int pageSize) {
        var (items, totalCount) = await bookingRepository.GetPagedByUserIdAsync(userId, page, pageSize);
        return new PagedResult<Booking> {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public Task<Booking?> GetByIdAsync(Guid id) => bookingRepository.GetByIdAsync(id);
    
    public Task<List<Booking>> GetByEventIdAsync(Guid eventId) => bookingRepository.GetByEventIdAsync(eventId);
    
    public async Task<PagedResult<Booking>> GetPagedByEventIdAsync(Guid evenId, int page, int pageSize) {
        var (items, totalCount) = await bookingRepository.GetPagedByEventIdAsync(evenId, page, pageSize);
        return new PagedResult<Booking> {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
    
    public async Task<Booking?> CancelAsync(Guid bookingId, Guid requestingUserId) {
        var booking = await bookingRepository.GetByIdAsync(bookingId);
        if (booking == null) return null;

        if (booking.UserId != requestingUserId) {
            throw new UnauthorizedAccessException("You can only cancel your own bookings");
        }

        if (booking.Status == BookingStatus.Cancelled) {
            throw new InvalidOperationException("Booking is already cancelled");
        }
        
        await unitOfWork.BeginTransactionAsync();

        try {
            booking.Status = BookingStatus.Cancelled;
            bookingRepository.Update(booking);
            await bookingRepository.SaveChangesAsync();

            await ticketRepository.ReleaseAsync(booking.TicketId, booking.Quantity);

            await unitOfWork.CommitAsync();
            return booking;
        }
        catch {
            await unitOfWork.RollbackAsync();
            throw;
        }
    }
    
    public async Task ExpireAbandonedBookingAsync(TimeSpan expiryThreshold) {
        var cutoff = DateTime.UtcNow - expiryThreshold;
        var expiredBookings = await bookingRepository.GetExpiredPendingBookingsAsync(cutoff);

        foreach (var booking in expiredBookings) {
            await unitOfWork.BeginTransactionAsync();
            try {
                booking.Status = BookingStatus.Cancelled;
                bookingRepository.Update(booking);
                await ticketRepository.ReleaseAsync(booking.TicketId, booking.Quantity);
                await bookingRepository.SaveChangesAsync();
                await unitOfWork.CommitAsync();
            }
            catch {
                await unitOfWork.RollbackAsync();
            }
        }
    }
}