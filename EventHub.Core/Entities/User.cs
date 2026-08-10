using EventHub.Core.Enums;

namespace EventHub.Core.Entities;

public class User: BaseEntity {
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}