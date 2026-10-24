using EventHub.Core.Events;

namespace EventHub.Core.SavedEvents;

public interface ISavedEventRepository {
    /// <summary>Ids of the events the user has saved.</summary>
    Task<List<string>> GetEventIdsAsync(string userId);

    /// <summary>The saved events, most recently saved first.</summary>
    Task<List<Event>> GetEventsAsync(string userId, int limit);

    /// <summary>Saves the event. Does nothing if it is already saved.</summary>
    Task AddAsync(string userId, string eventId);

    /// <summary>Removes the event from the user's saved events, if it is there.</summary>
    Task RemoveAsync(string userId, string eventId);
}
