using System.ComponentModel.DataAnnotations;

namespace Quotely.Api.DTOs;

public record BusinessProfileDto
{
    public Guid Id { get; init; }
    public string BusinessName { get; init; } = string.Empty;
    public string? BusinessEmail { get; init; }
    public string? Phone { get; init; }
    public string? AddressLine { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? TaxNumber { get; init; }
    public string? LogoUrl { get; init; }
    public string Currency { get; init; } = "INR";
}

public record UpdateBusinessProfileRequest
{
    [Required, MaxLength(200)]
    public string BusinessName { get; init; } = string.Empty;

    [EmailAddress, MaxLength(256)]
    public string? BusinessEmail { get; init; }

    [MaxLength(50)] public string? Phone { get; init; }
    [MaxLength(400)] public string? AddressLine { get; init; }
    [MaxLength(120)] public string? City { get; init; }
    [MaxLength(120)] public string? State { get; init; }
    [MaxLength(30)] public string? PostalCode { get; init; }
    [MaxLength(120)] public string? Country { get; init; }
    [MaxLength(60)] public string? TaxNumber { get; init; }
    [MaxLength(500_000)] public string? LogoUrl { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = "INR";
}
