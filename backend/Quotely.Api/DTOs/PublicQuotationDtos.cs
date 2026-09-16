using System.ComponentModel.DataAnnotations;

namespace Quotely.Api.DTOs;

/// <summary>
/// What a customer holding the share link is allowed to see. Deliberately carries no database
/// identifiers, no user or business id, and nothing about the token.
/// </summary>
public record PublicQuotationDto
{
    public string QuotationNumber { get; init; } = string.Empty;
    public DateOnly QuotationDate { get; init; }
    public DateOnly ValidUntil { get; init; }

    public PublicBusinessDto Business { get; init; } = new();
    public PublicCustomerDto Customer { get; init; } = new();
    public IReadOnlyList<PublicQuotationItemDto> Items { get; init; } = Array.Empty<PublicQuotationItemDto>();

    public decimal Subtotal { get; init; }
    public decimal DiscountTotal { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal GrandTotal { get; init; }
    public string Currency { get; init; } = "INR";

    public string? Notes { get; init; }
    public string? Terms { get; init; }

    public string Status { get; init; } = string.Empty;
    /// <summary>True once the valid-until date has passed; the page then hides the response buttons.</summary>
    public bool IsExpired { get; init; }
    /// <summary>True when the customer may still accept or reject.</summary>
    public bool CanRespond { get; init; }

    public DateTime? RespondedAt { get; init; }
    public string? RespondedByName { get; init; }
}

public record PublicBusinessDto
{
    public string BusinessName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? AddressLine { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? TaxNumber { get; init; }
    public string? LogoUrl { get; init; }
}

public record PublicCustomerDto
{
    public string Name { get; init; } = string.Empty;
    public string? CompanyName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? AddressLine { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}

public record PublicQuotationItemDto
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Unit { get; init; } = "Service";
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Discount { get; init; }
    public decimal TaxRate { get; init; }
    public decimal LineTotal { get; init; }
}

/// <summary>Body for both accept and reject; the decision comes from the route, never the payload.</summary>
public record PublicResponseRequest
{
    [Required(ErrorMessage = "Please enter your name."), MaxLength(150)]
    public string Name { get; init; } = string.Empty;

    [EmailAddress(ErrorMessage = "Enter a valid email address."), MaxLength(256)]
    public string? Email { get; init; }

    [MaxLength(1000, ErrorMessage = "Please keep the comment under 1000 characters.")]
    public string? Comment { get; init; }
}

/// <summary>
/// Returned once, when the owner creates the link. The raw token is never persisted, so this
/// response is the only moment the URL exists — which is why the ready-to-send share material is
/// composed into it here rather than left to a second call that could never see the token again.
/// </summary>
public record PublicQuotationLinkDto(string Url, DateTime CreatedAt, ShareDto Share);
