using EventHub.Core.Interfaces;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Repositories;

public class Repository<T>(AppDbContext db): IRepository<T> where T: class {
    protected readonly AppDbContext Db = db;
    protected readonly DbSet<T> DbSet = db.Set<T>();

    public async Task<T?> GetByIdAsync(Guid id) => await DbSet.FindAsync(id);
    
    public async Task<List<T>> GetAllAsync() => await DbSet.ToListAsync();
    
    public async Task<(List<T> Items, int TotalCount)> GetPagedAsync(int page, int pageSize) {
        var totalCount = await DbSet.CountAsync();
        var items = await DbSet
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }
    
    public async Task AddAsync(T entity) => await DbSet.AddAsync(entity);
    
    public void Update(T entity) => DbSet.Update(entity);
    
    public void Delete(T entity) => DbSet.Remove(entity);
    
    public async Task<int>  SaveChangesAsync() => await Db.SaveChangesAsync();
}   