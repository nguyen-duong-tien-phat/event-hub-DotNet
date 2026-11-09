using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Infrastructure.Common;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace EventHub.Infrastructure.Events;

public class EventRepository(AppDbContext db): Repository<Event>(db), IEventRepository {
    public async Task<Event?> GetEventDetailByIdAsync(string id) {
        var eventEntity = await DbSet
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Include(e => e.Organizer)
            .FirstOrDefaultAsync();
        return eventEntity;
    }

    public async Task<(List<Event> Items, int TotalCount)> GetEventsQuery(
        int page,
        int pageSize,
        string? search,
        string? category,
        decimal? minPrice,
        decimal? maxPrice,
        DateTime? from,
        DateTime? to,
        string? organizerId,
        EventSort sort,
        CursorKey? after = null
        ) {
        var query = DbSet.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(organizerId)) {
            query = query.Where(e => e.OrganizerId == organizerId);
        }

        if (!string.IsNullOrWhiteSpace(search)) {
            // Case-insensitive, like the cache key (it lowercases the text): a plain Contains is case-sensitive in
            // Postgres, so "neon" missed "Neon Nights" and a cached "Night" answer was served for "night".
            // %, _ and \ are escaped so they match themselves.
            var pattern = $"%{search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.Title, pattern) ||
                EF.Functions.ILike(e.Location, pattern) ||
                EF.Functions.ILike(e.Description, pattern));
        }

        if (!string.IsNullOrWhiteSpace(category)) {
            var categoryNormalized = category.Trim().ToLower();
            query = query.Where(e => e.Category.ToLower() == categoryNormalized);
        }

        if (minPrice != null || maxPrice != null) {
            // Price filters look at the cheapest ticket, so events without tickets drop out.
            query = query.Where(e => e.Tickets.Any());
            if (minPrice != null) query = query.Where(e => e.Tickets.Min(t => t.Price) >= minPrice.Value);
            if (maxPrice != null) query = query.Where(e => e.Tickets.Min(t => t.Price) <= maxPrice.Value);
        }

        if (from != null) {
            query = query.Where(e => e.StartsAt >= from.Value);
        }
        if (to != null) {
            query = query.Where(e => e.StartsAt <= to.Value);
        }

        var totalCount = await query.CountAsync();
        // The id breaks ties, so pages never overlap.
        var ordered = sort switch {
            EventSort.StartsAtDesc => query.OrderByDescending(e => e.StartsAt),
            EventSort.Newest => query.OrderByDescending(e => e.CreatedAt),
            _ => query.OrderBy(e => e.StartsAt),
        };
        var ordered2 = ordered.ThenBy(e => e.Id);
        // Keyset: the rows past the cursor in this sort's own direction (ids always ascend). Same order as the OFFSET page.
        var paged = after is not { } k ? ordered2.Skip((page - 1) * pageSize) : sort switch {
            EventSort.StartsAtDesc => ordered2.Where(e => e.StartsAt < k.At || (e.StartsAt == k.At && string.Compare(e.Id, k.Id) > 0)),
            EventSort.Newest => ordered2.Where(e => e.CreatedAt < k.At || (e.CreatedAt == k.At && string.Compare(e.Id, k.Id) > 0)),
            _ => ordered2.Where(e => e.StartsAt > k.At || (e.StartsAt == k.At && string.Compare(e.Id, k.Id) > 0)),
        };
        var items = await paged
            .Take(pageSize + 1)
            .Include(e => e.Organizer)
            .Include(e => e.Tickets)
            .ToListAsync();

        return (items, totalCount);
    }

    // Per event, with every sum in its own CTE: joining the jsonb ticket lines to the bookings would repeat each booking's
    // revenue once per line. "Sold" and "revenue" mean what EventStatsDto means: confirmed bookings only, fee not included.
    private const string SalesPerEvent = """
        WITH ev AS (
            SELECT e."Id", e."Title", e."StartsAt", e."Location", e."ImageUrl", e."Category" FROM "Events" e
            WHERE @org::text IS NULL OR e."OrganizerId" = @org
        ),
        seats AS (
            SELECT t."EventId", SUM(t."TotalQuantity")::int AS total, SUM(t."RemainingQuantity")::int AS remaining
            FROM "Tickets" t WHERE t."EventId" IN (SELECT "Id" FROM ev) GROUP BY t."EventId"
        ),
        paid AS (
            SELECT b."EventId", SUM(b."TotalPrice" - b."ServiceFee") AS revenue
            FROM "Bookings" b WHERE b."Status" = 'Confirmed' AND b."EventId" IN (SELECT "Id" FROM ev) GROUP BY b."EventId"
        ),
        sold AS (
            SELECT b."EventId", SUM((j ->> 'Quantity')::int)::int AS qty
            FROM "Bookings" b CROSS JOIN LATERAL jsonb_array_elements(b."Tickets") AS j
            WHERE b."Status" = 'Confirmed' AND b."EventId" IN (SELECT "Id" FROM ev) GROUP BY b."EventId"
        ),
        checked AS (
            SELECT b."EventId", COUNT(*)::int AS n
            FROM "Admissions" a JOIN "Bookings" b ON b."Id" = a."BookingId"
            WHERE a."CheckedInAt" IS NOT NULL AND b."EventId" IN (SELECT "Id" FROM ev) GROUP BY b."EventId"
        ),
        per AS (
            SELECT ev."Id", ev."Title", ev."StartsAt", ev."Location", ev."ImageUrl", ev."Category",
                COALESCE(seats.total, 0) AS "TotalQuantity", COALESCE(seats.remaining, 0) AS "RemainingQuantity",
                COALESCE(sold.qty, 0) AS "TicketsSold", COALESCE(paid.revenue, 0) AS "TicketRevenue",
                COALESCE(checked.n, 0) AS "CheckedIn"
            FROM ev
            LEFT JOIN seats ON seats."EventId" = ev."Id" LEFT JOIN paid ON paid."EventId" = ev."Id"
            LEFT JOIN sold ON sold."EventId" = ev."Id" LEFT JOIN checked ON checked."EventId" = ev."Id"
        )
        """;

    public async Task<(SalesSummaryDto Summary, List<EventSalesDto> Events)> GetSalesAsync(string? organizerId, int page, int pageSize, DateTime now) {
        // A fresh set per command: a parameter object cannot be shared between two.
        NpgsqlParameter[] Params(params NpgsqlParameter[] extra) => [
            new("org", NpgsqlDbType.Text) { Value = (object?)organizerId ?? DBNull.Value },
            new("now", NpgsqlDbType.TimestampTz) { Value = now },
            ..extra];

        var summary = (await db.Database.SqlQueryRaw<SalesSummaryDto>(SalesPerEvent + """
            SELECT COUNT(*)::int AS "Events",
                (COUNT(*) FILTER (WHERE "StartsAt" > @now))::int AS "Upcoming",
                (COUNT(*) FILTER (WHERE "StartsAt" <= @now))::int AS "Past",
                COALESCE(SUM("TicketsSold"), 0)::int AS "TicketsSold",
                COALESCE(SUM("TicketRevenue"), 0) AS "TicketRevenue",
                COALESCE(SUM("CheckedIn"), 0)::int AS "CheckedIn",
                COALESCE(SUM("TotalQuantity") FILTER (WHERE "StartsAt" <= @now), 0)::int AS "TicketsOfPastEvents",
                COALESCE(SUM("TicketsSold") FILTER (WHERE "StartsAt" <= @now), 0)::int AS "TicketsSoldOfPastEvents"
            FROM per
            """, Params()).ToListAsync()).Single();

        var events = await db.Database.SqlQueryRaw<EventSalesDto>(SalesPerEvent + """
            SELECT "Id", "Title", "StartsAt", "Location", "ImageUrl", "Category", "TotalQuantity", "RemainingQuantity", "TicketsSold", "TicketRevenue", "CheckedIn"
            FROM per ORDER BY "StartsAt" DESC, "Id" LIMIT @take OFFSET @skip
            """, Params(new NpgsqlParameter("take", pageSize), new NpgsqlParameter("skip", (page - 1) * pageSize))).ToListAsync();

        return (summary, events);
    }

    public async Task<List<Event>> GetFeaturedEventsAsync(int limit) {
        var items = await DbSet
            .AsNoTracking()
            .Where(e => e.StartsAt > DateTime.UtcNow)
            .Where(e => e.Tickets.Any())
            .OrderBy(e => (double)e.Tickets.Sum(t => t.RemainingQuantity) / e.Tickets.Sum(t => t.TotalQuantity))
            .ThenByDescending(e => e.Tickets.Sum(t => t.TotalQuantity - t.RemainingQuantity))
            .Take(limit)
            .Include(e => e.Organizer)
            .Include(e => e.Tickets)
            .ToListAsync();
        return items;
    }
    
    public async Task<List<string>> GetUpcomingCategoriesAsync() =>
        await DbSet
            .AsNoTracking()
            .Where(e => e.StartsAt > DateTime.UtcNow)
            .Select(e => e.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

    public async Task<List<Event>> GetUpcomingEventsAsync(int limit) {
        var items = await DbSet
            .AsNoTracking()
            .Where(e => e.StartsAt > DateTime.UtcNow)
            .OrderBy(e => e.StartsAt)
            .Take(limit)
            .Include(e => e.Organizer)
            .Include(e => e.Tickets)
            .ToListAsync();
        return items;
    }
}