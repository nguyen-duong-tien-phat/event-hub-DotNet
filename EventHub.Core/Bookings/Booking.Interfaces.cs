using EventHub.Core.Common;

namespace EventHub.Core.Bookings;

/// <summary>Booking numbers for one event, as the repository counts them.</summary>
public class BookingStats {
    public int Pending { get; set; }
    public int Confirmed { get; set; }
    public int Cancelled { get; set; }

    /// <summary>Total charged by confirmed bookings, service fee included.</summary>
    public decimal ConfirmedTotal { get; set; }
    public decimal ConfirmedServiceFees { get; set; }

    /// <summary>Quantity per ticket id, over confirmed bookings only.</summary>
    public Dictionary<string, int> SoldByTicketId { get; set; } = [];
    public int CheckedIn { get; set; }
}

public interface IBookingRepository : IRepository<Booking> {
    Task<List<Booking>> GetByUserIdAsync(string userId);
    Task<(List<Booking> Items, int TotalCount)> GetPagedByUserIdAsync(string userId, int page, int pageSize);
    Task<List<Booking>> GetByEventIdAsync(string eventId);
    Task<bool> AnyForEventAsync(string eventId);
    Task<BookingStats> GetStatsForEventAsync(string eventId);
    Task<(List<Booking> Items, int TotalCount)> GetPagedByEventIdAsync(string eventId, int page, int pageSize);
    /// <summary>Forgets entities from a failed save so the context can be used for a fresh query.</summary>
    void DetachPending();
    Task<Booking?> GetByIdempotencyKeyAsync(string userId, string key);
    Task<Booking?> GetByPaymentIntentId(string paymentIntentId);
    Task<List<Booking>> GetExpiredPendingBookingsAsync(DateTime olderThan);
}