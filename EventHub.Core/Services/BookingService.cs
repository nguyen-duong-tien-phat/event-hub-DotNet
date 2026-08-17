using EventHub.Core.Common;
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
        var ticket = await ticketRepository.GetByIdAsync(request.TicketId);
        if (ticket == null) {
            throw new KeyNotFoundException("Ticket not found");
        }
        
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
                Quantity = request.Quantity,
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
}