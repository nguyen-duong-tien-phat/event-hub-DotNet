using EventHub.Core.Events;

namespace EventHub.Core.SavedEvents;

public class SavedEventService(ISavedEventRepository savedEventRepository, IEventRepository eventRepository) {
    public Task<List<string>> GetEventIdsAsync(string userId) =>
        savedEventRepository.GetEventIdsAsync(userId);

    public Task<List<Event>> GetEventsAsync(string userId, int limit) =>
        savedEventRepository.GetEventsAsync(userId, limit);

    /// <returns>False if the event does not exist.</returns>
    public async Task<bool> SaveAsync(string userId, string eventId) {
        if (await eventRepository.GetByIdAsync(eventId) == null) return false;

        await savedEventRepository.AddAsync(userId, eventId);
        return true;
    }

    public Task RemoveAsync(string userId, string eventId) =>
        savedEventRepository.RemoveAsync(userId, eventId);
}
