using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Payments;
using EventHub.Core.Tickets;
using EventHub.Core.Users;

namespace EventHub.Core.Bookings;

/// <summary>The same request is still being processed; the client should retry shortly.</summary>
public class BookingInProgressException() : Exception("This booking is still being created. Please retry in a moment.");

public class BookingService (
    IBookingRepository bookingRepository, 
    ITicketRepository ticketRepository,
    IEventRepository eventRepository,
    IUnitOfWork unitOfWork,
    IPaymentService paymentService)
{
    /// <summary>How long a pending booking keeps its tickets before it can expire.</summary>
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(15);

    /// <summary>Service fee charged on top of the tickets: 1%.</summary>
    public const decimal ServiceFeeRate = 0.01m;

    public async Task<BookingWithPaymentResult?> CreateAsync(CreateBookingRequest request) {
        // Same user + same key = same intent: hand back the original booking instead of holding tickets again.
        if (request.IdempotencyKey != null) {
            var replay = await TryReplayAsync(request.UserId, request.IdempotencyKey);
            if (replay != null) return replay;
        }

        List<BookingTicket>  tickets = [];
        foreach (var item in request.Tickets) {
            var ticket = await ticketRepository.GetByIdAsync(item.TicketId);
            if (ticket == null) {
                throw new KeyNotFoundException("Ticket not found");
            }
            if (ticket.EventId != request.EventId) {
                throw new ArgumentException("Ticket does not belong to this event.");
            }
            if (item.Quantity < 1 || item.Quantity > ticket.MaxPerOrder) {
                throw new ArgumentException($"You can book between 1 and {ticket.MaxPerOrder} of '{ticket.Type}'.");
            }
            tickets.Add(new BookingTicket {
                TicketId = item.TicketId,
                TicketType = ticket.Type,
                Quantity = item.Quantity,
                UnitPrice = ticket.Price,
            });
        }

        Booking booking;
        
        decimal subtotal = 0;
        foreach (var item in tickets) {
            subtotal += item.Quantity * item.UnitPrice;
        }
        var serviceFee = Math.Round(subtotal * ServiceFeeRate, 2, MidpointRounding.AwayFromZero);
        var totalPrice = subtotal + serviceFee;
        
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
                IdempotencyKey = request.IdempotencyKey,
                Tickets = tickets,
                // Free bookings need no payment, so they are confirmed right away.
                Status = totalPrice == 0 ? BookingStatus.Confirmed : BookingStatus.Pending,
                ServiceFee = serviceFee,
                TotalPrice = totalPrice
            };
            
            await bookingRepository.AddAsync(booking);
            await bookingRepository.SaveChangesAsync();

            await unitOfWork.CommitAsync();
        }
        catch {
            await unitOfWork.RollbackAsync();

            // Two requests with the same key can both pass the check at the top. The unique index
            // lets only one insert win; the loser lands here (its ticket hold is rolled back).
            // If the key now exists, the loser simply returns the winner's booking.
            if (request.IdempotencyKey != null) {
                bookingRepository.DetachPending();
                var replay = await TryReplayAsync(request.UserId, request.IdempotencyKey);
                if (replay != null) return replay;
            }
            throw;
        }
        
        if (totalPrice == 0) {
            return new BookingWithPaymentResult { Booking = booking, ClientSecret = string.Empty };
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

    /// <summary>The booking already made for this user + key, or null if the key is new.</summary>
    private async Task<BookingWithPaymentResult?> TryReplayAsync(string userId, string key) {
        var existing = await bookingRepository.GetByIdempotencyKeyAsync(userId, key);
        if (existing == null) return null;

        var secret = string.Empty;
        if (existing.Status == BookingStatus.Pending) {
            // The winning request has saved the booking but not created the payment yet.
            if (existing.PaymentIntentId == null) {
                throw new BookingInProgressException();
            }
            secret = await paymentService.GetClientSecretAsync(existing.PaymentIntentId);
        }
        return new BookingWithPaymentResult { Booking = existing, ClientSecret = secret, Replayed = true };
    }

    /// <summary>The booking as sent to clients, with its event and ticket names.</summary>
    public async Task<BookingResponseDto> ToResponseAsync(Booking booking, string? clientSecret = null) =>
        ToResponse(booking, await eventRepository.GetByIdAsync(booking.EventId), clientSecret);

    /// <summary>
    /// Several bookings at once. They are mapped one after another, never in parallel: the
    /// repositories share one DbContext per request, which allows a single query at a time.
    /// Each event is loaded once, however many bookings point at it.
    /// </summary>
    public async Task<List<BookingResponseDto>> ToResponsesAsync(IEnumerable<Booking> bookings) {
        var events = new Dictionary<string, Event?>();
        var responses = new List<BookingResponseDto>();

        foreach (var booking in bookings) {
            if (!events.TryGetValue(booking.EventId, out var ev)) {
                ev = await eventRepository.GetByIdAsync(booking.EventId);
                events[booking.EventId] = ev;
            }
            responses.Add(ToResponse(booking, ev));
        }

        return responses;
    }

    private static BookingResponseDto ToResponse(Booking booking, Event? ev, string? clientSecret = null) => new() {
        Id = booking.Id,
        EventId = booking.EventId,
        Event = ev == null ? null : new BookingEventDto {
            Id = ev.Id,
            Title = ev.Title,
            StartsAt = ev.StartsAt,
            Location = ev.Location,
            ImageUrl = ev.ImageUrl,
        },
        Tickets = booking.Tickets.Select(t => new BookingTicketItemDto {
            TicketId = t.TicketId,
            Type = t.TicketType,
            Quantity = t.Quantity,
            UnitPrice = t.UnitPrice,
        }).ToList(),
        Quantity = booking.Tickets.Sum(t => t.Quantity),
        Subtotal = booking.TotalPrice - booking.ServiceFee,
        ServiceFee = booking.ServiceFee,
        TotalPrice = booking.TotalPrice,
        Status = booking.Status.ToString(),
        CreatedAt = booking.CreatedAt,
        ExpiresAt = booking.CreatedAt + HoldDuration,
        ClientSecret = string.IsNullOrEmpty(clientSecret) ? null : clientSecret,
    };

    /// <summary>
    /// Asks Stripe whether the booking has been paid and confirms it if so. Lets the
    /// app finish a payment without waiting for the Stripe webhook.
    /// </summary>
    public async Task<Booking?> ConfirmPaymentAsync(string bookingId, string requestingUserId) {
        var booking = await bookingRepository.GetByIdAsync(bookingId);
        if (booking == null) return null;

        if (booking.UserId != requestingUserId) {
            throw new UnauthorizedAccessException();
        }

        if (booking.Status == BookingStatus.Pending
            && booking.PaymentIntentId != null
            && await paymentService.IsPaymentSucceededAsync(booking.PaymentIntentId)) {
            booking.Status = BookingStatus.Confirmed;
            bookingRepository.Update(booking);
            await bookingRepository.SaveChangesAsync();
        }

        if (booking.Status == BookingStatus.Cancelled) {
            await RefundIfPaidAsync(booking);
        }

        return booking;
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
        }
        catch {
            await unitOfWork.RollbackAsync();
            throw;
        }
        // The booking is canceled for good; stopping the payment comes after.
        await StopPaymentAsync(booking);
        return booking;
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
                continue;
            }
            try {
                await StopPaymentAsync(booking);
            } catch {
                // Canceled already; the next confirm or webhook refunds if it was paid.
            }
        }
    }
    
    public async Task HandlePaymentSucceeded(string paymentIntentId) {
        var booking = await bookingRepository.GetByPaymentIntentId(paymentIntentId);
        
        if (booking == null) return;
        
        if (booking.Status == BookingStatus.Cancelled) {
            await RefundIfPaidAsync(booking);
            return;
        }
        
        if (booking.Status != BookingStatus.Pending) return;

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
    
    /// <summary>
    /// Called once a booking is cancelled: stops the payment, and if the customer
    /// had already paid (they pressed Pay just before we cancelled), refunds them.
    /// </summary>
    private async Task StopPaymentAsync(Booking booking) {
        if (booking.PaymentIntentId == null) return; // free booking: nothing was charged

        var cancelled = await paymentService.CancelPaymentIntentAsync(booking.PaymentIntentId);
        if (!cancelled) {
            await RefundIfPaidAsync(booking);
        }
    }

    private async Task RefundIfPaidAsync(Booking booking) {
        if (booking.PaymentIntentId == null) return;
        if (await paymentService.IsPaymentSucceededAsync(booking.PaymentIntentId)) {
            await paymentService.RefundAsync(booking.PaymentIntentId, booking.Id);
        }
    }
}