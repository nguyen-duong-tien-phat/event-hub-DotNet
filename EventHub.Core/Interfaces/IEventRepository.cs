using EventHub.Core.Entities;

namespace EventHub.Core.Interfaces;

public interface IEventRepository: IRepository<Event> {
    Task<Event?> GetEventDetailByIdAsync(string id);
}