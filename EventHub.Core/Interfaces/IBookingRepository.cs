using EventHub.Core.Common;
using EventHub.Core.Entities;

namespace EventHub.Core.Interfaces;

public interface IBookingRepository : IRepository<Booking> {
    Task<List<Booking>> GetByUserIdAsync(string userId);
    Task<(List<Booking> Items, int TotalCount)> GetPagedByUserIdAsync(string userId, int page, int pageSize);
    Task<List<Booking>> GetByEventIdAsync(string eventId);
    Task<(List<Booking> Items, int TotalCount)> GetPagedByEventIdAsync(string eventId, int page, int pageSize);
    Task<Booking?> GetByPaymentIntentId(string paymentIntentId);
    Task<List<Booking>> GetExpiredPendingBookingsAsync(DateTime olderThan);
}