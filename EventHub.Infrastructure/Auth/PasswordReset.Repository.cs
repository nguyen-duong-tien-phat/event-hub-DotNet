using EventHub.Core.Auth;
using EventHub.Infrastructure.Common;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Auth;

public class PasswordResetRepository(AppDbContext db) : Repository<PasswordResetCode>(db), IPasswordResetRepository {
    public Task<PasswordResetCode?> GetActiveAsync(string userId) {
        var now = DateTime.UtcNow;
        return DbSet
            .Where(c => c.UserId == userId && c.UsedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> TryCountAttemptAsync(string id, int maxAttempts) {
        var rows = await DbSet
            .Where(c => c.Id == id && c.UsedAt == null && c.Attempts < maxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1));
        return rows == 1;
    }

    public async Task<bool> TryConsumeAsync(string id) {
        var now = DateTime.UtcNow;
        var rows = await DbSet
            .Where(c => c.Id == id && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now));
        return rows == 1;
    }

    public async Task InvalidateAllForUserAsync(string userId) {
        var now = DateTime.UtcNow;
        await DbSet
            .Where(c => c.UserId == userId && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now));
    }

    public Task<int> DeleteExpiredAsync(DateTime expiredBefore) =>
        DbSet.Where(c => c.ExpiresAt < expiredBefore).ExecuteDeleteAsync();
}
