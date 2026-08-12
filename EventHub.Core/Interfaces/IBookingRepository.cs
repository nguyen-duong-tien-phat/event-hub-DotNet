using EventHub.Core.Entities;

namespace EventHub.Core.Interfaces;

public interface IBookingRepository : IRepository<Booking> {
    Task<List<Booking>> GetByUserIdAsync(Guid userId);
    Task<List<Booking>> GetByEventIdAsync(Guid eventId);
}