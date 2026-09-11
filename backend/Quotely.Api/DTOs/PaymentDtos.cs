using System.ComponentModel.DataAnnotations;
using Quotely.Api.Models;

namespace Quotely.Api.DTOs;

/// <summary>
/// The financial state of an invoice, always derived from recorded payments rather than from a
/// stored, editable figure.
/// </summary>
public record PaymentSummaryDto
{
    public decimal Total { get; init; }
    public decimal Paid { get; init; }
    public decimal Outstanding { get; init; }
    public string Currency { get; init; } = "INR";
    public string InvoiceStatus { get; init; } = nameof(Models.InvoiceStatus.Draft);
    /// <summary>True when a customer holding the public link may start a payment right now.</summary>
    public bool CanPay { get; init; }
    /// <summary>Set when money is held but not yet settled, so the UI can say "confirming".</summary>
    public bool HasPendingPayment { get; init; }
}

/// <summary>One payment attempt, as shown to the business owner. Carries no sensitive data.</summary>
public record PaymentDto
{
    public Guid Id { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "INR";
    public string Status { get; init; } = nameof(PaymentStatus.Created);
    public string Provider { get; init; } = string.Empty;
    /// <summary>The provider's payment reference, safe to display and to quote in support.</summary>
    public string? Reference { get; init; }
    public string? OrderReference { get; init; }
    public string? Method { get; init; }
    public string? FailureReason { get; init; }
    public DateTime? PaidAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record InvoicePaymentsDto
{
    public PaymentSummaryDto Summary { get; init; } = new();
    public IReadOnlyList<PaymentDto> Payments { get; init; } = Array.Empty<PaymentDto>();
}

// ---- public (customer-facing) -----------------------------------------

/// <summary>
/// What a customer holding the payment link may see. No user id, business id, customer id or
/// invoice id — the same rule the public quotation payload follows.
/// </summary>
public record PublicInvoiceDto
{
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateOnly InvoiceDate { get; init; }
    public DateOnly DueDate { get; init; }

    public PublicBusinessDto Business { get; init; } = new();
    public PublicCustomerDto Customer { get; init; } = new();
    public IReadOnlyList<PublicInvoiceItemDto> Items { get; init; } = Array.Empty<PublicInvoiceItemDto>();

    public decimal Subtotal { get; init; }
    public decimal DiscountTotal { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal GrandTotal { get; init; }
    public string Currency { get; init; } = "INR";

    public string? Notes { get; init; }
    public string? Terms { get; init; }

    public string Status { get; init; } = string.Empty;
    public bool IsOverdue { get; init; }

    public decimal Paid { get; init; }
    public decimal Outstanding { get; init; }
    public bool CanPay { get; init; }
    public bool HasPendingPayment { get; init; }

    /// <summary>Settled payments only, so the customer sees what has been credited.</summary>
    public IReadOnlyList<PublicPaymentDto> Payments { get; init; } = Array.Empty<PublicPaymentDto>();
}

public record PublicInvoiceItemDto
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

public record PublicPaymentDto
{
    public decimal Amount { get; init; }
    public string? Method { get; init; }
    public string? Reference { get; init; }
    public DateTime? PaidAt { get; init; }
}

/// <summary>
/// Everything the browser needs to open checkout. Note what is absent: the request that produces
/// this carries no amount, because the server decides what may be collected.
/// </summary>
public record PaymentOrderDto
{
    public string KeyId { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    /// <summary>Minor units (paise), matching what was registered with the provider.</summary>
    public long Amount { get; init; }
    public string Currency { get; init; } = "INR";
    public string InvoiceNumber { get; init; } = string.Empty;
    public string BusinessName { get; init; } = string.Empty;
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CustomerContact { get; init; }
}

/// <summary>What checkout handed the browser. Every value here is untrusted until verified.</summary>
public record VerifyPaymentRequest
{
    [Required, MaxLength(80)] public string RazorpayPaymentId { get; init; } = string.Empty;
    [Required, MaxLength(80)] public string RazorpayOrderId { get; init; } = string.Empty;
    [Required, MaxLength(256)] public string RazorpaySignature { get; init; } = string.Empty;
}

/// <summary>The server's verdict, which is the only thing the customer's page may believe.</summary>
public record VerifyPaymentResponse
{
    public bool Success { get; init; }
    /// <summary>Our internal payment status: Captured, Pending or Failed.</summary>
    public string PaymentStatus { get; init; } = string.Empty;
    public string InvoiceStatus { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public decimal Paid { get; init; }
    public decimal Outstanding { get; init; }
    public decimal AmountPaid { get; init; }
    public string Currency { get; init; } = "INR";
    public string? PaymentReference { get; init; }
    public string? Message { get; init; }
}

public record PublicInvoiceLinkDto(string Url, DateTime CreatedAt);
