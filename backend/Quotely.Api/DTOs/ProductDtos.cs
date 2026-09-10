using System.ComponentModel.DataAnnotations;

namespace Quotely.Api.DTOs;

public record ProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Unit { get; init; } = "Service";
    public decimal Price { get; init; }
    public decimal TaxRate { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record SaveProductRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(1000)] public string? Description { get; init; }

    [Required, MaxLength(50)]
    public string Unit { get; init; } = "Service";

    [Range(0, 999_999_999)]
    public decimal Price { get; init; }

    [Range(0, 100)]
    public decimal TaxRate { get; init; }
}
