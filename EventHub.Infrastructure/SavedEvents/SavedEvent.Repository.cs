using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.SavedEvents;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.SavedEvents;

public class SavedEventRepository(AppDbContext db) : ISavedEventRepository {
    public Task<List<string>> GetEventIdsAsync(string userId) =>
        db.SavedEvents
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.EventId)
            .ToListAsync();

    public async Task<List<Event>> GetEventsAsync(string userId, int limit) {
        // Include has to come before any projection, so load the saved rows with
        // their event and organizer, then take the events.
        var saved = await db.SavedEvents
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(limit)
            .Include(s => s.Event!)
            .ThenInclude(e => e.Organizer)
            .Include(s => s.Event!)
            .ThenInclude(e => e.Tickets)
            .ToListAsync();

        return saved.Select(s => s.Event!).ToList();
    }

    // The unique (UserId, EventId) index plus ON CONFLICT makes saving twice a no-op.
    public async Task AddAsync(string userId, string eventId) {
        var id = IdGenerator.Generate("saved");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SavedEvents" ("Id", "UserId", "EventId", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {userId}, {eventId}, now(), now())
            ON CONFLICT ("UserId", "EventId") DO NOTHING
            """);
    }

    public async Task RemoveAsync(string userId, string eventId) {
        await db.SavedEvents
            .Where(s => s.UserId == userId && s.EventId == eventId)
            .ExecuteDeleteAsync();
    }
}
