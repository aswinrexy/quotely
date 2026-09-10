using System.ComponentModel.DataAnnotations;

namespace Quotely.Api.DTOs;

public record CustomerDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? CompanyName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? AddressLine { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record SaveCustomerRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(200)] public string? CompanyName { get; init; }
    [EmailAddress, MaxLength(256)] public string? Email { get; init; }
    [MaxLength(50)] public string? Phone { get; init; }
    [MaxLength(400)] public string? AddressLine { get; init; }
    [MaxLength(120)] public string? City { get; init; }
    [MaxLength(120)] public string? State { get; init; }
    [MaxLength(30)] public string? PostalCode { get; init; }
    [MaxLength(120)] public string? Country { get; init; }
    [MaxLength(2000)] public string? Notes { get; init; }
}
