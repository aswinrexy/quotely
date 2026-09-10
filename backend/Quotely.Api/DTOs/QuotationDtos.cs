using System.ComponentModel.DataAnnotations;
using Quotely.Api.Models;

namespace Quotely.Api.DTOs;

public record QuotationItemDto
{
    public Guid Id { get; init; }
    public Guid? ProductId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Unit { get; init; } = "Service";
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Discount { get; init; }
    public decimal TaxRate { get; init; }
    public decimal LineSubtotal { get; init; }
    public decimal LineTax { get; init; }
    public decimal LineTotal { get; init; }
}

public record QuotationListItemDto
{
    public Guid Id { get; init; }
    public string QuotationNumber { get; init; } = string.Empty;
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public DateOnly QuotationDate { get; init; }
    public DateOnly ValidUntil { get; init; }
    public string Status { get; init; } = nameof(QuotationStatus.Draft);
    public decimal GrandTotal { get; init; }
    public string Currency { get; init; } = "INR";
}

public record QuotationDto
{
    public Guid Id { get; init; }
    public string QuotationNumber { get; init; } = string.Empty;
    public CustomerDto Customer { get; init; } = null!;
    public BusinessProfileDto? Business { get; init; }
    public DateOnly QuotationDate { get; init; }
    public DateOnly ValidUntil { get; init; }
    public string? Notes { get; init; }
    public string? Terms { get; init; }
    public string Status { get; init; } = nameof(QuotationStatus.Draft);
    public decimal Subtotal { get; init; }
    public decimal DiscountTotal { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal GrandTotal { get; init; }
    public string Currency { get; init; } = "INR";
    public IReadOnlyList<QuotationItemDto> Items { get; init; } = Array.Empty<QuotationItemDto>();
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record SaveQuotationItemRequest
{
    public Guid? ProductId { get; init; }

    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(1000)] public string? Description { get; init; }

    [Required, MaxLength(50)]
    public string Unit { get; init; } = "Service";

    [Range(0.0001, 1_000_000)]
    public decimal Quantity { get; init; }

    [Range(0, 999_999_999)]
    public decimal UnitPrice { get; init; }

    [Range(0, 999_999_999)]
    public decimal Discount { get; init; }

    [Range(0, 100)]
    public decimal TaxRate { get; init; }
}

public record SaveQuotationRequest
{
    [Required]
    public Guid CustomerId { get; init; }

    [Required]
    public DateOnly QuotationDate { get; init; }

    [Required]
    public DateOnly ValidUntil { get; init; }

    [MaxLength(2000)] public string? Notes { get; init; }
    [MaxLength(4000)] public string? Terms { get; init; }

    /// <summary>One of: Draft, Sent, Accepted, Rejected, Expired. Defaults to Draft.</summary>
    public string? Status { get; init; }

    [Required, MinLength(1)]
    public List<SaveQuotationItemRequest> Items { get; init; } = new();
}

public record DashboardStatsDto(
    int TotalQuotations,
    int DraftCount,
    int SentCount,
    int AcceptedCount,
    decimal TotalValue,
    string Currency);
