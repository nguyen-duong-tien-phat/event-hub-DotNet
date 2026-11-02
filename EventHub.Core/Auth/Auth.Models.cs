using System.ComponentModel.DataAnnotations;

namespace EventHub.Core.Auth;

public class LoginDto {
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class AuthResponseDto {
    public string Id {get; set;} = string.Empty;
    /// <summary>The short-lived access token, sent as "Authorization: Bearer".</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Trades for a new pair at POST /auth/refresh. Works once.</summary>
    public string RefreshToken { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class RegisterDto {
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string Password { get; set; } = string.Empty;
    [Required]
    public string FullName { get; set; } = string.Empty;
}

public class RefreshRequestDto {
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
