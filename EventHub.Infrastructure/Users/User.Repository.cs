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
