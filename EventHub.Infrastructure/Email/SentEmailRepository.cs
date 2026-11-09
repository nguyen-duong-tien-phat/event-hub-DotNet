using EventHub.Core.Email;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Email;

public class SentEmailRepository(AppDbContext db) : ISentEmailRepository {
    public async Task<bool> TryClaimAsync(string key) {
        // One statement, and the rows-affected count is the verdict: of two callers with the same key, one inserts and one does not.
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO "SentEmails" ("Key", "CreatedAt") VALUES ({key}, now())
          ON CONFLICT ("Key") DO NOTHING
          """);
        return rows == 1;
    }

    public Task ReleaseAsync(string key) => db.SentEmails.Where(e => e.Key == key).ExecuteDeleteAsync();
}