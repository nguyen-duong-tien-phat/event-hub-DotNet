using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EventHub.Core.Users;

namespace EventHub.Tests;

/// <summary>The role is optional when an admin creates a user. Left out, the account is an Attendee, never an Admin (which is 0).</summary>
public class CreateUserDtoTests {
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web); // what MVC uses

    private static bool IsAccepted(string json, out UserRole? role) {
        var dto = JsonSerializer.Deserialize<CreateUserDto>(json, Web)!;
        role = dto.Role;
        return Validator.TryValidateObject(dto, new ValidationContext(dto), [], validateAllProperties: true);
    }

    private const string Account = "\"email\":\"a@mail.test\",\"fullName\":\"An\",\"password\":\"password123\"";

    [Fact]
    public void ARoleLeftOut_IsAccepted_AndStaysNull() {
        Assert.True(IsAccepted("{" + Account + "}", out var role));
        Assert.Null(role);
    }

    [Theory]
    [InlineData("Organizer", UserRole.Organizer)]
    [InlineData("Attendee", UserRole.Attendee)]
    [InlineData("Admin", UserRole.Admin)]
    [InlineData("organizer", UserRole.Organizer)] // any letter case
    public void AKnownRoleName_IsAccepted(string name, UserRole expected) {
        Assert.True(IsAccepted("{" + Account + $",\"role\":\"{name}\"" + "}", out var role));
        Assert.Equal(expected, role);
    }

    [Fact]
    public void ANumberThatIsNoRole_IsRefused() {
        Assert.False(IsAccepted("{" + Account + ",\"role\":7}", out _));
    }

    [Fact]
    public void ANameThatIsNoRole_CannotBeRead() {
        Assert.Throws<JsonException>(() => IsAccepted("{" + Account + ",\"role\":\"Owner\"}", out _)); // MVC answers 400
    }
}
