using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Payments;
using EventHub.Core.Tickets;
using EventHub.Core.Files;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

/// <summary>Keyset paging: the cursor, and what the services hand to the repositories and give back.</summary>
public class CursorPagingTests {
    private static readonly DateTime At = new(2026, 11, 7, 12, 30, 15, DateTimeKind.Utc);

    [Fact]
    public void ACursor_RoundTripsItsTimeAndId() {
        var at = At.AddTicks(1230); // microsecond precision, like the database
        var key = Cursor.Decode(Cursor.Encode(at, "booking_a|b"))!.Value;

        Assert.Equal(at, key.At);
        Assert.Equal(DateTimeKind.Utc, key.At.Kind);
        Assert.Equal("booking_a|b", key.Id);
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("abc")] // base64 of nonsense
    [InlineData("bm90LXRpY2tz")] // "not-ticks": no separator
    [InlineData("eHx4")] // "x|x": ticks are not a number
    [InlineData("OTk5OTk5OTk5OTk5OTk5OTk5OXxp")] // "999...9|i": ticks out of range
    public void AMalformedCursor_IsRefused(string cursor) {
        Assert.Throws<InvalidCursorException>(() => Cursor.Decode(cursor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoCursor_IsNull(string? cursor) => Assert.Null(Cursor.Decode(cursor));

    [Fact]
    public void From_DropsTheExtraRow_AndPointsTheCursorAtTheLastKeptOne() {
        var page = PagedResult<string>.From(["a", "b", "c"], 1, 2, 9, s => "after-" + s);

        Assert.Equal(["a", "b"], page.Items);
        Assert.Equal("after-b", page.NextCursor);
    }

    [Fact]
    public void From_OnTheLastPage_HasNoNextCursor() {
        var page = PagedResult<string>.From(["a", "b"], 1, 2, 2, s => "after-" + s);

        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Events_PassTheDecodedCursorToTheRepository_AndReturnTheNextOne() {
        var repo = new Mock<IEventRepository>();
        var cache = new Mock<ICacheService>();
        var keys = new List<string>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>())).Callback<string>(keys.Add).ReturnsAsync((string?)null);
        var rows = Enumerable.Range(1, 3).Select(i => new Event {
            Id = $"event_{i}", Title = "T", Description = "D", Location = "L", OrganizerId = "o", ImageUrl = "u",
            Highlights = [], StartsAt = At.AddDays(i), CreatedAt = At,
        }).ToList();
        repo.Setup(r => r.GetEventsQuery(1, 2, null, null, null, null, null, null, null, EventSort.StartsAt, new CursorKey(At, "event_0")))
            .ReturnsAsync((rows, 9));
        repo.Setup(r => r.GetEventsQuery(1, 2, null, null, null, null, null, null, null, EventSort.StartsAt, null)).ReturnsAsync((new List<Event>(), 0));
        var service = new EventService(repo.Object, new Mock<ITicketRepository>().Object, new Mock<IBookingRepository>().Object,
            cache.Object, new UploadService(new Mock<IFileStorage>().Object), new Mock<IUserRepository>().Object);

        var page = await service.GetPagedAsync(1, 2, null, null, null, null, null, null, cursor: Cursor.Encode(At, "event_0"));
        await service.GetPagedAsync(1, 2, null, null, null, null, null, null);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(Cursor.Encode(At.AddDays(2), "event_2"), page.NextCursor); // StartsAt order: the key is StartsAt
        Assert.Equal(2, keys.Distinct().Count()); // a page with a cursor is cached apart from the first page
    }

    [Fact]
    public async Task Events_WithAMalformedCursor_ThrowBeforeAnyQuery() {
        var repo = new Mock<IEventRepository>();
        var service = new EventService(repo.Object, new Mock<ITicketRepository>().Object, new Mock<IBookingRepository>().Object,
            new Mock<ICacheService>().Object, new UploadService(new Mock<IFileStorage>().Object), new Mock<IUserRepository>().Object);

        await Assert.ThrowsAsync<InvalidCursorException>(() =>
            service.GetPagedAsync(1, 10, null, null, null, null, null, null, cursor: "!!!"));

        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ManagedBookings_PassTheDecodedCursor_AndReturnTheNextOne() {
        var repo = new Mock<IBookingRepository>();
        var rows = Enumerable.Range(1, 3).Select(i => new Booking { Id = $"booking_{i}", EventId = "e", UserId = "u", CreatedAt = At.AddMinutes(-i) }).ToList();
        repo.Setup(r => r.GetPagedAsync(null, null, null, null, 1, 2, new CursorKey(At, "booking_0"))).ReturnsAsync((rows, 5));
        var service = new BookingService(repo.Object, new Mock<ITicketRepository>().Object, new Mock<IEventRepository>().Object,
            new Mock<IUnitOfWork>().Object, new Mock<IPaymentService>().Object);

        var page = await service.GetManagedPagedAsync("admin", UserRole.Admin, null, null, null, 1, 2, Cursor.Encode(At, "booking_0"));

        Assert.Equal(["booking_1", "booking_2"], page.Items.Select(b => b.Id));
        Assert.Equal(Cursor.Encode(At.AddMinutes(-2), "booking_2"), page.NextCursor);
    }

    [Fact]
    public async Task Admissions_PassTheDecodedCursor_AndTheKeyIsTheCheckInTimeWhenOnlyCheckedInAreListed() {
        var admissions = new Mock<IAdmissionRepository>();
        var events = new Mock<IEventRepository>();
        events.Setup(r => r.GetByIdAsync("e")).ReturnsAsync(new Event {
            Id = "e", Title = "T", Description = "D", Location = "L", OrganizerId = "org", ImageUrl = "u", Highlights = [], StartsAt = At,
        });
        var user = new User { Id = "u", Email = "u@x", FullName = "U" };
        List<EventAdmissionRow> Rows() => Enumerable.Range(1, 3).Select(i => new EventAdmissionRow(
            new Admission { Id = $"adm_{i}", BookingId = "b", TicketId = "t", TicketType = "General", Code = "ABCDEFGH", CreatedAt = At.AddHours(-i), CheckedInAt = At.AddMinutes(-i) },
            null, user)).ToList();
        admissions.Setup(r => r.GetPagedByEventAsync("e", true, null, 1, 2, new CursorKey(At, "adm_0"))).ReturnsAsync((Rows(), 7));
        admissions.Setup(r => r.GetPagedByEventAsync("e", null, null, 1, 2, null)).ReturnsAsync((Rows(), 7));
        var service = new AdmissionService(admissions.Object, events.Object);

        var checkedIn = await service.GetPagedByEventAsync("org", UserRole.Organizer, "e", true, null, 1, 2, Cursor.Encode(At, "adm_0"));
        var all = await service.GetPagedByEventAsync("org", UserRole.Organizer, "e", null, null, 1, 2);

        Assert.Equal(Cursor.Encode(At.AddMinutes(-2), "adm_2"), checkedIn!.NextCursor);
        Assert.Equal(Cursor.Encode(At.AddHours(-2), "adm_2"), all!.NextCursor);
    }
}
