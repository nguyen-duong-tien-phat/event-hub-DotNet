using EventHub.Core.Common;
using EventHub.Core.Entities;

namespace EventHub.Core.Interfaces;

public interface IBookingRepository : IRepository<Booking> {
    Task<List<Booking>> GetByUserIdAsync(Guid userId);
    Task<(List<Booking> Items, int TotalCount)> GetPagedByUserIdAsync(Guid userId, int page, int pageSize);
    Task<List<Booking>> GetByEventIdAsync(Guid eventId);
    Task<(List<Booking> Items, int TotalCount)> GetPagedByEventIdAsync(Guid eventId, int page, int pageSize);
    Task<Booking?> GetByPaymentIntentId(string paymentIntentId);
    Task<List<Booking>> GetExpiredPendingBookingsAsync(DateTime olderThan);
}