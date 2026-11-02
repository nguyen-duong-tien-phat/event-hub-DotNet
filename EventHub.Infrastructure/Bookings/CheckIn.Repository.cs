using EventHub.Core.Bookings;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Bookings;

public class CheckInRepository(AppDbContext db) : ICheckInRepository {
    public async Task<bool> TryAddAsync(TicketCheckIn checkIn) {
        // One statement: insert unless the (booking, number) row exists. Two scans of the same
        // ticket at the same moment cannot both insert, and nothing throws for the loser.
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TicketCheckIns" ("Id", "BookingId", "Number", "CheckedInBy", "CreatedAt", "UpdatedAt")
            VALUES ({checkIn.Id}, {checkIn.BookingId}, {checkIn.Number}, {checkIn.CheckedInBy}, {checkIn.CreatedAt}, {checkIn.UpdatedAt})
            ON CONFLICT ("BookingId", "Number") DO NOTHING
            """);
        return rows == 1;
    }

    public Task<TicketCheckIn?> GetAsync(string bookingId, int number) =>
        db.TicketCheckIns.AsNoTracking().FirstOrDefaultAsync(c => c.BookingId == bookingId && c.Number == number);
}
