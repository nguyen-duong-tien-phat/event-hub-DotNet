using EventHub.Core.Enums;

namespace EventHub.Core.Services.Models;

public class CreateUserRequest {
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UserRole? Role { get; set; }
}