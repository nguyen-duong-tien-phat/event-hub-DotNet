namespace EventHub.Core.Interfaces;

public interface IRepository<T> where T: class {
    Task<T?> GetByIdAsync(Guid id);
    Task<List<T>> GetAllAsync();
    Task<(List<T> Items, int TotalCount)> GetPagedAsync(int page, int pageSize);
    Task AddAsync(T entity);
    void Update(T entity);
    void Delete(T entity);
    Task<int> SaveChangesAsync();
}