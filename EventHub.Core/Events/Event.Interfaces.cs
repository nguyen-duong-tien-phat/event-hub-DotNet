using EventHub.Core.Common;

namespace EventHub.Core.Events;

public interface IEventRepository: IRepository<Event> {
    Task<Event?> GetEventDetailByIdAsync(string id);
}