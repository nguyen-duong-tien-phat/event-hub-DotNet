using EventHub.Core.Entities;

namespace EventHub.Core.Interfaces;

public interface IEventRepository: IRepository<Event> {
    Task<(List<Event> Items, int TotalCount)> GetPagedWithOrganizerAsync(int page, int pageSize);
}