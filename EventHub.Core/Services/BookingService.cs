using EventHub.Core.Entities;
using EventHub.Core.Enums;
using EventHub.Core.Interfaces;
using EventHub.Core.Services.Models;

namespace EventHub.Core.Services;

public class BookingService (
    IBookingRepository bookingRepository, 
    ITicketRepository ticketRepository,
    IUnitOfWork unitOfWork) 
{
    public async Task<Booking?> CreateAsync(CreateBookingRequest request) {
        await unitOfWork.BeginTransactionAsync();
        try {
            var reserved = await ticketRepository.TryReserveAsync(request.TicketId, request.Quantity);
            if (!reserved) { // sold out, or not enough remaining
                await unitOfWork.RollbackAsync();
                return null;
            } 

            var booking = new Booking {
                UserId = request.UserId,
                TicketId = request.TicketId,
                Status = BookingStatus.Confirmed
            };

            await bookingRepository.AddAsync(booking);
            await bookingRepository.SaveChangesAsync();
            await unitOfWork.CommitAsync();
            return booking;
        }
        catch (Exception e) {
            await unitOfWork.RollbackAsync();
            throw;
        }
    }

    public Task<List<Booking>> GetByUserIdAsync(Guid userId) => bookingRepository.GetByUserIdAsync(userId);

    public Task<Booking?> GetByIdAsync(Guid id) => bookingRepository.GetByIdAsync(id);
    
    public Task<List<Booking>> GetByEventIdAsync(Guid eventId) => bookingRepository.GetByEventIdAsync(eventId);
}