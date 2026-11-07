using EventHub.Core.Bookings;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Bookings;

public class AdmissionRepository(AppDbContext db) : IAdmissionRepository {
    public async Task IssueAsync(Booking booking) {
        // Without a ticket number there is no unique key that stops two issues at once, so take a lock per booking
        // and look again inside it: the later caller finds the rows the first one made.
        // shortcut: a short code that collides (about 1 in 10^12 per pair) fails this call and the next one makes new codes.
        await using var tx = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync() : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({booking.Id}))");

        if (!await db.Admissions.AnyAsync(a => a.BookingId == booking.Id)) {
            db.Admissions.AddRange(booking.Tickets.SelectMany(t => Enumerable.Range(0, t.Quantity).Select(_ => new Admission {
                BookingId = booking.Id, TicketId = t.TicketId, TicketType = t.TicketType,
            })));
            await db.SaveChangesAsync();
        }

        if (tx != null) await tx.CommitAsync();
    }

    public Task<Admission?> FindByIdAsync(string id) =>
        db.Admissions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

    public Task<Admission?> FindByCodeAsync(string code) =>
        db.Admissions.AsNoTracking().FirstOrDefaultAsync(a => a.Code == code);

    public async Task<bool> TryCheckInAsync(string admissionId, string staffId) {
        // One statement, and the rows-affected count is the verdict: two scans at once cannot both match "not yet used".
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Admissions" SET "CheckedInAt" = now(), "CheckedInBy" = {staffId}, "UpdatedAt" = now()
            WHERE "Id" = {admissionId} AND "CheckedInAt" IS NULL
            """);
        return rows == 1;
    }

    public Task<bool> AnyCheckedInAsync(string bookingId) =>
        db.Admissions.AnyAsync(a => a.BookingId == bookingId && a.CheckedInAt != null);

    public async Task<List<AdmissionRow>> ListByBookingIdsAsync(IEnumerable<string> bookingIds) {
        var ids = bookingIds.ToList();
        return await (from a in db.Admissions.AsNoTracking()
                      where ids.Contains(a.BookingId)
                      join s in db.Users on a.CheckedInBy equals s.Id into staff
                      from s in staff.DefaultIfEmpty()
                      orderby a.CreatedAt, a.Id
                      select new AdmissionRow(a, s == null ? null : s.FullName)).ToListAsync();
    }

    public async Task<(List<EventAdmissionRow> Items, int TotalCount)> GetPagedByEventAsync(
        string eventId, bool? checkedIn, string? search, int page, int pageSize) {
        var query = from a in db.Admissions.AsNoTracking()
                    join b in db.Bookings on a.BookingId equals b.Id
                    where b.EventId == eventId && b.Status == BookingStatus.Confirmed
                    join u in db.Users on b.UserId equals u.Id
                    join s in db.Users on a.CheckedInBy equals s.Id into staff
                    from s in staff.DefaultIfEmpty()
                    select new { a, u, StaffName = s == null ? null : s.FullName };

        if (checkedIn != null) query = query.Where(x => (x.a.CheckedInAt != null) == checkedIn.Value);
        if (!string.IsNullOrWhiteSpace(search)) {
            var text = search.Trim().ToLowerInvariant();
            query = query.Where(x => x.a.Code.ToLower().Contains(text)
                || x.u.FullName.ToLower().Contains(text) || x.u.Email.ToLower().Contains(text));
        }

        var totalCount = await query.CountAsync();
        // The id breaks ties, so pages never overlap. Who got in last comes first.
        var ordered = checkedIn == true
            ? query.OrderByDescending(x => x.a.CheckedInAt).ThenBy(x => x.a.Id)
            : query.OrderByDescending(x => x.a.CreatedAt).ThenBy(x => x.a.Id);
        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return (rows.Select(x => new EventAdmissionRow(x.a, x.StaffName, x.u)).ToList(), totalCount);
    }
}
