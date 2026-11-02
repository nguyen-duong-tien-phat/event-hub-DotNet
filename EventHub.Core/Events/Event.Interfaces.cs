using EventHub.Core.Common;

namespace EventHub.Core.Events;

public interface IEventRepository: IRepository<Event> {
    Task<Event?> GetEventDetailByIdAsync(string id);

    Task<(List<Event> Items, int TotalCount)> GetEventsQuery(
        int page,
        int pageSize,
        string? search,
        string? category,
        decimal? minPrice,
        decimal? maxPrice,
        DateTime? from,
        DateTime? to
        );

    Task<List<Event>> GetFeaturedEventsAsync(int limit = 5);
    Task<List<Event>> GetUpcomingEventsAsync(int limit = 5);

    /// <summary>The distinct categories of events that have not started yet, A to Z.</summary>
    Task<List<string>> GetUpcomingCategoriesAsync();
}