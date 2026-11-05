using EventHub.Core.Users;
using EventHub.Infrastructure.Common;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EventHub.Infrastructure.Users;

public class UserRepository(AppDbContext db): Repository<User>(db), IUserRepository {
    public Task<User?> GetByEmailAsync(string email) {
        var normalized = email.Trim().ToLowerInvariant();
        return DbSet.FirstOrDefaultAsync(x => x.Email == normalized);
    }

    // shortcut: a LIKE '%text%' scan, fine for thousands of users; add a pg_trgm index if the table grows far beyond that.
    public async Task<(List<User> Items, int TotalCount)> SearchPagedAsync(string? search, bool? isActive, int page, int pageSize) {
        var query = DbSet.AsNoTracking();
        if (isActive != null) query = query.Where(u => u.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search)) {
            var text = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.Email.ToLower().Contains(text) || u.FullName.ToLower().Contains(text));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return (items, totalCount);
    }

    public async Task<bool> TryAddAsync(User user) {
        await DbSet.AddAsync(user);
        try {
            await Db.SaveChangesAsync();
            return true;
        } catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) {
            Db.Entry(user).State = EntityState.Detached; // keep this request's DbContext usable
            return false;
        }
    }
}
