using EventHub.Core.Entities;

namespace EventHub.DTOs;

public class CreateUserDto {
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}

public class UserResponseDto {
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public static UserResponseDto FromEntity(User user) => new() {
        Id = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        Role = user.Role.ToString(),
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };
}