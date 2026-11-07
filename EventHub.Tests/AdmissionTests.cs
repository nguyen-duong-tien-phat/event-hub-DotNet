using EventHub.Core.Bookings;
using EventHub.Core.Events;
using EventHub.Core.Users;
using Moq;

namespace EventHub.Tests;

public class AdmissionTests {
    [Fact]
    public void NewCode_IsEightCharactersFromTheAlphabet_AndNormalizesToItself() {
        for (var i = 0; i < 50; i++) {
            var code = Admission.NewCode();
            Assert.Equal(8, code.Length);
            Assert.Equal(code, Admission.NormalizeCode(code));
        }
    }

    [Theory]
    [InlineData("k7q4-9xmd", "K7Q49XMD")]
    [InlineData(" K7Q4 9XMD ", "K7Q49XMD")]
    [InlineData("O0IL1234", "00111234")] // Crockford: O is 0, I and L are 1
    public void NormalizeCode_ReadsWhatAPersonTypes(string typed, string expected) =>
        Assert.Equal(expected, Admission.NormalizeCode(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SHORT")]
    [InlineData("K7Q49XMDK")]
    [InlineData("K7Q49XM!")]
    [InlineData("UUUUUUUU")] // no U in the alphabet
    public void NormalizeCode_RejectsAnythingElse(string? typed) =>
        Assert.Null(Admission.NormalizeCode(typed));

    private readonly Mock<IAdmissionRepository> _admissions = new();
    private readonly Mock<IEventRepository> _events = new();

    private AdmissionService Service() {
        _events.Setup(r => r.GetByIdAsync("event_1")).ReturnsAsync(new Event {
            Id = "event_1", Title = "T", Description = "D", Location = "L", OrganizerId = "org_1",
            ImageUrl = "u", Highlights = [], StartsAt = DateTime.UtcNow.AddDays(30),
        });
        _admissions.Setup(r => r.GetPagedByEventAsync("event_1", true, "ann", 1, 10)).ReturnsAsync(([], 4));
        return new AdmissionService(_admissions.Object, _events.Object);
    }

    [Theory]
    [InlineData("org_1", UserRole.Organizer)]
    [InlineData("admin_1", UserRole.Admin)]
    public async Task TheEventsOrganizerAndAnAdmin_SeeTheAttendees(string userId, UserRole role) {
        var result = await Service().GetPagedByEventAsync(userId, role, "event_1", true, "ann", 1, 10);

        Assert.Equal(4, result!.TotalCount);
    }

    [Fact]
    public async Task AnotherOrganizer_IsRefused_AndNothingIsQueried() {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Service().GetPagedByEventAsync("org_2", UserRole.Organizer, "event_1", null, null, 1, 10));

        _admissions.Verify(r => r.GetPagedByEventAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AnUnknownEvent_IsNull() {
        _events.Setup(r => r.GetByIdAsync("gone")).ReturnsAsync((Event?)null);

        Assert.Null(await Service().GetPagedByEventAsync("admin_1", UserRole.Admin, "gone", null, null, 1, 10));
    }
}
