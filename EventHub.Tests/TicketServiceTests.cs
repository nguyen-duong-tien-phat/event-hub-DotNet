using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

public class TicketServiceTests {
    private readonly Mock<ITicketRepository> _ticketRepo = new();
    private readonly Mock<IEventRepository> _eventRepo = new();
    private readonly Mock<ICacheService> _cache = new();

    private TicketService CreateService() => new(_ticketRepo.Object, _eventRepo.Object, _cache.Object);

    private void GivenEventOwnedBy(string organizerId) =>
        _eventRepo.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(new Event {
            Id = "event_1", Title = "T", Description = "D", Location = "L", OrganizerId = organizerId,
            ImageUrl = "u", Highlights = [], StartsAt = DateTime.UtcNow.AddDays(30),
        });

    private static CreateTicketRequest NewRequest() => new() {
        EventId = "event_1", Type = "General", Price = 10m, TotalQuantity = 5, MaxPerOrder = 2,
    };

    private Ticket GivenTicket() {
        var ticket = new Ticket { Id = "ticket_1", EventId = "event_1", Price = 10m, TotalQuantity = 5, RemainingQuantity = 5, MaxPerOrder = 2 };
        _ticketRepo.Setup(r => r.GetByIdAsync("ticket_1")).ReturnsAsync(ticket);
        return ticket;
    }

    [Fact]
    public async Task CreateAsync_ByTheEventsOrganizer_CreatesAFullTicket() {
        GivenEventOwnedBy("org_1");

        var ticket = await CreateService().CreateAsync("org_1", UserRole.Organizer, NewRequest());

        Assert.NotNull(ticket);
        Assert.Equal(5, ticket.RemainingQuantity);
        _ticketRepo.Verify(r => r.AddAsync(ticket), Times.Once);
        _ticketRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ByAnAdmin_Creates() {
        GivenEventOwnedBy("org_1");

        Assert.NotNull(await CreateService().CreateAsync("admin_1", UserRole.Admin, NewRequest()));
    }

    [Fact]
    public async Task CreateAsync_ByAnotherOrganizer_ThrowsAndAddsNothing() {
        GivenEventOwnedBy("org_1");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateService().CreateAsync("org_2", UserRole.Organizer, NewRequest()));

        _ticketRepo.Verify(r => r.AddAsync(It.IsAny<Ticket>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ForAnEventThatDoesNotExist_ReturnsNullAndAddsNothing() {
        Assert.Null(await CreateService().CreateAsync("org_1", UserRole.Organizer, NewRequest()));

        _ticketRepo.Verify(r => r.AddAsync(It.IsAny<Ticket>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ByTheEventsOrganizer_ChangesThePrice() {
        GivenEventOwnedBy("org_1");
        var ticket = GivenTicket();

        await CreateService().UpdateAsync("org_1", UserRole.Organizer, "ticket_1", new UpdateTicketRequest { Price = 25m });

        Assert.Equal(25m, ticket.Price);
        _ticketRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ByAnotherOrganizer_ThrowsAndLeavesTheTicketAlone() {
        GivenEventOwnedBy("org_1");
        var ticket = GivenTicket();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService()
            .UpdateAsync("org_2", UserRole.Organizer, "ticket_1", new UpdateTicketRequest { Price = 25m }));

        Assert.Equal(10m, ticket.Price);
        _ticketRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ForATicketThatDoesNotExist_ReturnsNull() {
        Assert.Null(await CreateService().UpdateAsync("org_1", UserRole.Organizer, "missing", new UpdateTicketRequest { Price = 25m }));
    }

    [Fact]
    public async Task CreateAsync_ClearsTheCachedEventLists() {
        GivenEventOwnedBy("org_1");

        await CreateService().CreateAsync("org_1", UserRole.Organizer, NewRequest());

        _cache.Verify(c => c.RemoveByPrefixAsync("events:"), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NeverSavesTheWholeTicket_SoItCannotUndoTicketsSoldMeanwhile() {
        GivenEventOwnedBy("org_1");
        GivenTicket();

        await CreateService().UpdateAsync("org_1", UserRole.Organizer, "ticket_1", new UpdateTicketRequest { Price = 25m });

        _ticketRepo.Verify(r => r.Update(It.IsAny<Ticket>()), Times.Never);
        _cache.Verify(c => c.RemoveByPrefixAsync("events:"), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_RaisingTheTotal_AddsTheDifferenceToTheStockInOneStep() {
        GivenEventOwnedBy("org_1");
        GivenTicket(); // total 5
        _ticketRepo.Setup(r => r.TryAdjustTotalAsync("ticket_1", 3)).ReturnsAsync(true);

        await CreateService().UpdateAsync("org_1", UserRole.Organizer, "ticket_1", new UpdateTicketRequest { TotalQuantity = 8 });

        _ticketRepo.Verify(r => r.TryAdjustTotalAsync("ticket_1", 3), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_LoweringTheTotalBelowWhatIsSold_ThrowsAndChangesNothing() {
        GivenEventOwnedBy("org_1");
        var ticket = GivenTicket();
        _ticketRepo.Setup(r => r.TryAdjustTotalAsync("ticket_1", -4)).ReturnsAsync(false);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().UpdateAsync(
            "org_1", UserRole.Organizer, "ticket_1", new UpdateTicketRequest { TotalQuantity = 1, Price = 99m }));

        Assert.Equal(10m, ticket.Price);
        _ticketRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WithTheSameTotal_LeavesTheStockAlone() {
        GivenEventOwnedBy("org_1");
        GivenTicket(); // total 5

        await CreateService().UpdateAsync("org_1", UserRole.Organizer, "ticket_1", new UpdateTicketRequest { TotalQuantity = 5 });

        _ticketRepo.Verify(r => r.TryAdjustTotalAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_OfAnUnsoldTicket_DeletesItAndClearsTheCache() {
        GivenEventOwnedBy("org_1");
        GivenTicket();
        _ticketRepo.Setup(r => r.TryDeleteUnsoldAsync("ticket_1")).ReturnsAsync(true);

        Assert.True(await CreateService().DeleteAsync("org_1", UserRole.Organizer, "ticket_1"));

        _cache.Verify(c => c.RemoveByPrefixAsync("events:"), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_OfATicketWithSales_Throws_AndKeepsTheCache() {
        GivenEventOwnedBy("org_1");
        GivenTicket();
        _ticketRepo.Setup(r => r.TryDeleteUnsoldAsync("ticket_1")).ReturnsAsync(false);

        await Assert.ThrowsAsync<TicketHasSalesException>(
            () => CreateService().DeleteAsync("org_1", UserRole.Organizer, "ticket_1"));

        _cache.Verify(c => c.RemoveByPrefixAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ByAnotherOrganizer_ThrowsBeforeDeletingAnything() {
        GivenEventOwnedBy("org_1");
        GivenTicket();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateService().DeleteAsync("org_2", UserRole.Organizer, "ticket_1"));

        _ticketRepo.Verify(r => r.TryDeleteUnsoldAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_OfATicketThatDoesNotExist_ReturnsFalse() {
        Assert.False(await CreateService().DeleteAsync("org_1", UserRole.Organizer, "missing"));
    }
}
