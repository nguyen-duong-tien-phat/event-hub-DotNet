using EventHub.Core.Entities;
using EventHub.Core.Enums;
using EventHub.Core.Interfaces;
using EventHub.Core.Services.Models;

namespace EventHub.Core.Services;

public class BookingService(IBookingRepository bookingRepository, ITicketRepository ticketRepository) {
    public async Task<Booking?> CreateAsync(CreateBookingRequest request) {
        var reserved = await ticketRepository.TryReserveAsync(request.TicketId, request.Quantity);
        if (!reserved) return null; // sold out, or not enough remaining

        var booking = new Booking {
            UserId = request.UserId,
            TicketId = request.TicketId,
            Status = BookingStatus.Confirmed
        };

        await bookingRepository.AddAsync(booking);
        await bookingRepository.SaveChangesAsync();
        return booking;
    }

    public Task<List<Booking>> GetByUserIdAsync(Guid userId) => bookingRepository.GetByUserIdAsync(userId);

    public Task<Booking?> GetByIdAsync(Guid id) => bookingRepository.GetByIdAsync(id);
    
    public Task<List<Booking>> GetByEventIdAsync(Guid eventId) => bookingRepository.GetByEventIdAsync(eventId);
}