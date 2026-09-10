using System.ComponentModel.DataAnnotations;

namespace Quotely.Api.DTOs;

public record RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    [Required, MaxLength(150)]
    public string FullName { get; init; } = string.Empty;

    /// <summary>Optional: creates the business profile straight away so onboarding is one step.</summary>
    [MaxLength(200)]
    public string? BusinessName { get; init; }
}

public record LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

public record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, UserDto User);

public record UserDto(Guid Id, string Email, string FullName);
