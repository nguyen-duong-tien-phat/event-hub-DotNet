using EventHub.Core.Common;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Common;

public class Repository<T>(AppDbContext db): IRepository<T> where T: class {
    protected readonly AppDbContext Db = db;
    protected readonly DbSet<T> DbSet = db.Set<T>();

    public async Task<T?> GetByIdAsync(string id) => await DbSet.FindAsync(id);
    
    public async Task<List<T>> GetAllAsync() => await DbSet.ToListAsync();
    
    public async Task<(List<T> Items, int TotalCount)> GetPagedAsync(int page, int pageSize) {
        var totalCount = await DbSet.CountAsync();
        // Newest first; the id breaks ties, so pages never overlap. Every entity derives from BaseEntity.
        var items = await DbSet
            .OrderByDescending(e => EF.Property<DateTime>(e, "CreatedAt"))
            .ThenBy(e => EF.Property<string>(e, "Id"))
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