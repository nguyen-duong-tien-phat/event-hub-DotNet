using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Payments;
using EventHub.Core.Tickets;
using EventHub.Core.Users;

namespace EventHub.Core.Bookings;

public class BookingService (
    IBookingRepository bookingRepository, 
    ITicketRepository ticketRepository,
    IEventRepository eventRepository,
    IUnitOfWork unitOfWork,
    IPaymentService paymentService)
{
    public async Task<BookingWithPaymentResult?> CreateAsync(CreateBookingRequest request) {
        List<BookingTicket>  tickets = [];
        foreach (var item in request.Tickets) {
            var ticket = await ticketRepository.GetByIdAsync(item.TicketId);
            if (ticket == null) {
                throw new KeyNotFoundException("Ticket not found");
            }
            tickets.Add(new BookingTicket {
                TicketId = item.TicketId,
                Quantity = item.Quantity,
                UnitPrice = ticket.Price,
            });
        }

        Booking booking;
        
        decimal totalPrice = 0;
        foreach (var item in tickets) {
            totalPrice += item.Quantity * item.UnitPrice;
        }
        
        await unitOfWork.BeginTransactionAsync();
        
        // Create Booking
        try {
            var reserved = await ticketRepository.TryReserveAsync(tickets);
            if (!reserved) { // sold out, or not enough remaining
                await unitOfWork.RollbackAsync();
                return null;
            }
            
            booking = new Booking {
                EventId = request.EventId,
                UserId = request.UserId,
                Tickets = tickets,
                Status = BookingStatus.Pending,
                TotalPrice = totalPrice
            };
            
            await bookingRepository.AddAsync(booking);
            await bookingRepository.SaveChangesAsync();

            await unitOfWork.CommitAsync();
        }
        catch {
            await unitOfWork.RollbackAsync();
            throw;
        }
        
        // Payment
        try {
            var paymentIntent = await paymentService.CreatePaymentIntentAsync(totalPrice, "usd", booking.Id);

            booking.PaymentIntentId = paymentIntent.PaymentIntentId;
            bookingRepository.Update(booking);
            await bookingRepository.SaveChangesAsync();

            return new BookingWithPaymentResult {
                Booking = booking,
                ClientSecret = paymentIntent.ClientSecret
            };
        }
        catch (Exception ex) {
            booking.Status = BookingStatus.Cancelled;
            bookingRepository.Update(booking);
            await ticketRepository.ReleaseAsync(booking.Tickets);
            await bookingRepository.SaveChangesAsync();
            throw new InvalidOperationException("Failed to initialize payment. Your reservation has been released.", ex);
        }
    }

    public Task<List<Booking>> GetByUserIdAsync(string userId) => bookingRepository.GetByUserIdAsync(userId);
    
    public async Task<PagedResult<Booking>> GetPagedByUserIdAsync(string userId, int page, int pageSize) {
        var (items, totalCount) = await bookingRepository.GetPagedByUserIdAsync(userId, page, pageSize);
        return new PagedResult<Booking> {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<Booking?> GetByIdAsync(string id, string userId, UserRole userRole) {
        var booking = await bookingRepository.GetByIdAsync(id);
        if (booking == null) return null;

        if (userRole == UserRole.Attendee && booking.UserId != userId) {
            throw new UnauthorizedAccessException();
        }

        if (userRole == UserRole.Organizer) {
            var ev = await eventRepository.GetByIdAsync(booking.EventId);
            if (ev == null || ev.OrganizerId != userId) {
                throw new UnauthorizedAccessException();
            }
        }

        return booking;
    }

    public Task<List<Booking>> GetByEventIdAsync(string eventId) => bookingRepository.GetByEventIdAsync(eventId);
    
    public async Task<PagedResult<Booking>> GetPagedByEventIdAsync(string evenId, int page, int pageSize) {
        var (items, totalCount) = await bookingRepository.GetPagedByEventIdAsync(evenId, page, pageSize);
        return new PagedResult<Booking> {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
    
    public async Task<Booking?> CancelAsync(string bookingId, string requestingUserId) {
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

            await ticketRepository.ReleaseAsync(booking.Tickets);

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
                await ticketRepository.ReleaseAsync(booking.Tickets);
                await bookingRepository.SaveChangesAsync();
                await unitOfWork.CommitAsync();
            }
            catch {
                await unitOfWork.RollbackAsync();
            }
        }
    }
    
    public async Task HandlePaymentSucceeded(string paymentIntentId) {
        var booking = await bookingRepository.GetByPaymentIntentId(paymentIntentId);
        if (booking == null || booking.Status != BookingStatus.Pending) {
            return;
        }

        booking.Status = BookingStatus.Confirmed;
        bookingRepository.Update(booking);
        await bookingRepository.SaveChangesAsync();
    }

    public async Task HandlePaymentFailed(string paymentIntentId) {
        var booking = await bookingRepository.GetByPaymentIntentId(paymentIntentId);
        if (booking == null || booking.Status != BookingStatus.Pending) {
            return;
        }
        
        booking.Status = BookingStatus.Cancelled;
        bookingRepository.Update(booking);
        await ticketRepository.ReleaseAsync(booking.Tickets);
        await bookingRepository.SaveChangesAsync();
    }
}