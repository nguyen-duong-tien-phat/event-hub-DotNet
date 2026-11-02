using EventHub.Core.Auth;
using EventHub.Infrastructure.Common;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Auth;

public class RefreshTokenRepository(AppDbContext db) : Repository<RefreshToken>(db), IRefreshTokenRepository {
    public Task<RefreshToken?> GetByHashAsync(string tokenHash) =>
        DbSet.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

    public async Task<bool> TryRevokeAsync(string id) {
        var now = DateTime.UtcNow;
        var rows = await DbSet
            .Where(t => t.Id == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
        return rows == 1;
    }

    public async Task RevokeAllForUserAsync(string userId) {
        var now = DateTime.UtcNow;
        await DbSet
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
    }
}
