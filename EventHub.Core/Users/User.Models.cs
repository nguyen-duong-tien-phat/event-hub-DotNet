using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EventHub.Core.Users;

public class CreateUserRequest {
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UserRole? Role { get; set; }
}

// DTOs
public class UpdateUserRoleDto {
    /// <summary>Nullable and required on purpose: a missing value would otherwise become 0, which is Admin.</summary>
    [Required, EnumDataType(typeof(UserRole))]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UserRole? Role { get; set; }
}

public class UpdateUserActiveDto {
    /// <summary>Nullable and required on purpose: a missing value would otherwise become false, which blocks the account.</summary>
    [Required]
    public bool? IsActive { get; set; }
}

public class UserQuery : EventHub.Core.Common.PaginationQuery {
    /// <summary>Part of the name or the email, any letter case.</summary>
    public string? Search { get; set; }

    /// <summary>true: only active accounts; false: only blocked ones; left out: both.</summary>
    public bool? IsActive { get; set; }
}

public class CreateUserDto {
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required, MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
    public string Password { get; set; } = string.Empty;
}

public class UserResponseDto {
    public required string Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public static UserResponseDto FromEntity(User user) => new() {
        Id = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        Role = user.Role.ToString(),
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };
}
