using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EventHub.Core.Users;

namespace EventHub.Tests;

/// <summary>
/// How a request body becomes a role. The danger: UserRole.Admin is 0, so a missing or
/// unreadable value must never turn into an admin.
/// </summary>
public class UpdateUserRoleDtoTests {
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web); // what MVC uses

    private static bool IsAccepted(string json, out UserRole? role) {
        UpdateUserRoleDto? dto;
        try {
            dto = JsonSerializer.Deserialize<UpdateUserRoleDto>(json, Web);
        } catch (JsonException) {
            role = null;
            return false; // MVC answers 400 when the body cannot be read
        }

        role = dto!.Role;
        return Validator.TryValidateObject(dto, new ValidationContext(dto), [], validateAllProperties: true);
    }

    [Theory]
    [InlineData("""{"role":"Organizer"}""", UserRole.Organizer)]
    [InlineData("""{"role":"Attendee"}""", UserRole.Attendee)]
    [InlineData("""{"role":"Admin"}""", UserRole.Admin)]
    [InlineData("""{"role":"organizer"}""", UserRole.Organizer)] // any letter case
    public void AKnownRoleName_IsAccepted(string json, UserRole expected) {
        Assert.True(IsAccepted(json, out var role));
        Assert.Equal(expected, role);
    }

    [Theory]
    [InlineData("{}")]                  // missing
    [InlineData("""{"role":null}""")]
    [InlineData("""{"role":"Owner"}""")] // not a role
    [InlineData("""{"role":""}""")]
    [InlineData("""{"role":7}""")]       // a number that is no role
    public void AnythingElse_IsRefused_SoItCanNeverBecomeAdmin(string json) {
        var accepted = IsAccepted(json, out var role);

        Assert.False(accepted);
        Assert.NotEqual(UserRole.Admin, role);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"isActive":null}""")]
    public void TheActiveFlagMissing_IsRefused_SoItCanNeverBlockAnAccountByDefault(string json) {
        var dto = JsonSerializer.Deserialize<UpdateUserActiveDto>(json, Web)!;

        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), [], validateAllProperties: true));
    }

    [Theory]
    [InlineData("""{"isActive":false}""", false)]
    [InlineData("""{"isActive":true}""", true)]
    public void TheActiveFlag_IsAccepted(string json, bool expected) {
        var dto = JsonSerializer.Deserialize<UpdateUserActiveDto>(json, Web)!;

        Assert.True(Validator.TryValidateObject(dto, new ValidationContext(dto), [], validateAllProperties: true));
        Assert.Equal(expected, dto.IsActive);
    }
}
