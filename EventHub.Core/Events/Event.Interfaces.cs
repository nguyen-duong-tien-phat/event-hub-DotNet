using EventHub.Core.Common;

namespace EventHub.Core.Events;

public interface IEventRepository: IRepository<Event> {
    Task<Event?> GetEventDetailByIdAsync(string id);

    /// <summary>One page of events: up to pageSize + 1 rows (the extra shows there is a next page). With <paramref name="after"/> the page starts after that row, with no OFFSET.</summary>
    Task<(List<Event> Items, int TotalCount)> GetEventsQuery(
        int page,
        int pageSize,
        string? search,
        string? category,
        decimal? minPrice,
        decimal? maxPrice,
        DateTime? from,
        DateTime? to,
        string? organizerId,
        EventSort sort,
        CursorKey? after = null
        );

    /// <summary>
    /// Sales of the events of one organizer (or of all when null): the totals over every one of them and one page of
    /// per-event lines, newest event first. Computed in SQL, so no booking is loaded.
    /// </summary>
    Task<(SalesSummaryDto Summary, List<EventSalesDto> Events)> GetSalesAsync(string? organizerId, int page, int pageSize, DateTime now);

    Task<List<Event>> GetFeaturedEventsAsync(int limit = 5);
    Task<List<Event>> GetUpcomingEventsAsync(int limit = 5);

    /// <summary>The distinct categories of events that have not started yet, A to Z.</summary>
    Task<List<string>> GetUpcomingCategoriesAsync();
}