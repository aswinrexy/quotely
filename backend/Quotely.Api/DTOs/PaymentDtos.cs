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
    /// <summary>
    /// Non-zero only in the anomaly case where the provider captured more than the invoice total.
    /// Surfaced to the owner rather than hidden, because resolving it needs a human: V2.3 does
    /// not issue refunds.
    /// </summary>
    public decimal OverpaidBy { get; init; }
    public string Currency { get; init; } = "INR";
    public string InvoiceStatus { get; init; } = nameof(Models.InvoiceStatus.Draft);
    /// <summary>True when a customer holding the public link may start a payment right now.</summary>
    public bool CanPay { get; init; }
    /// <summary>Set when money is held but not yet settled, so the UI can say "confirming".</summary>
    public bool HasPendingPayment { get; init; }
}

/// <summary>One payment, as shown to the business owner. Carries no sensitive data.</summary>
public record PaymentDto
{
    public Guid Id { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "INR";
    public string Status { get; init; } = nameof(PaymentStatus.Created);
    /// <summary>"Gateway" or "Manual" — what the history badges the row with.</summary>
    public string Source { get; init; } = nameof(PaymentSource.Gateway);
    public string Provider { get; init; } = string.Empty;
    /// <summary>
    /// For a gateway payment, the provider's payment reference. For a manual one, whatever the
    /// owner typed — a cheque number or a bank UTR.
    /// </summary>
    public string? Reference { get; init; }
    public string? OrderReference { get; init; }
    public string? Method { get; init; }
    /// <summary>The owner's own note. Manual payments only; never shown to a customer.</summary>
    public string? Notes { get; init; }
    public string? FailureReason { get; init; }
    public DateTime? PaidAt { get; init; }
    public DateTime CreatedAt { get; init; }

    // ---- voiding (V2.5) ----
    public DateTime? VoidedAt { get; init; }
    public bool IsVoided { get; init; }
    /// <summary>True only for a manual payment that still stands, so the UI shows one Void action.</summary>
    public bool CanVoid { get; init; }
}

/// <summary>
/// Money the business received outside the gateway — cash, a bank transfer, a UPI transfer, a
/// cheque. Note what is absent: any total, balance or status. The server derives the outstanding
/// balance from the ledger and validates this amount against it.
/// </summary>
public record RecordManualPaymentRequest
{
    /// <summary>Must be greater than zero and no more than the current outstanding balance.</summary>
    [Range(0.01, 999_999_999)]
    public decimal Amount { get; init; }

    /// <summary>One of: cash, bank_transfer, upi, cheque, other.</summary>
    [Required, MaxLength(40)]
    public string Method { get; init; } = ManualPaymentMethods.Cash;

    /// <summary>
    /// When the money actually arrived. Back-dating is allowed and expected — an owner records
    /// last week's cash today — but a future date is not, since that money has not been received.
    /// Defaults to today when omitted.
    /// </summary>
    public DateOnly? PaymentDate { get; init; }

    /// <summary>Cheque number, bank UTR, or whatever the owner reconciles against. Optional.</summary>
    [MaxLength(100)] public string? Reference { get; init; }

    [MaxLength(500)] public string? Notes { get; init; }
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

/// <summary>
/// The freshly minted payment link, plus everything needed to hand it to the customer. The URL is
/// present exactly once — only its hash is stored — so the share material is composed here, in the
/// same response, rather than requiring a second call that could never see the token again.
/// </summary>
public record PublicInvoiceLinkDto(string Url, DateTime CreatedAt, InvoiceShareDto Share);

/// <summary>
/// Ready-made deep links for handing an invoice to a customer (V2.4).
///
/// Quotely sends nothing itself: <see cref="WhatsAppUrl"/> opens WhatsApp and
/// <see cref="MailtoUrl"/> opens the owner's own mail client, both with the message already
/// written. No WhatsApp Business API, no mail server, no delivery guarantee.
///
/// The only identifier any of these carry is the public invoice URL. No token is exposed on its
/// own, and no internal id — user, customer, invoice, payment or provider — appears anywhere.
/// </summary>
public record InvoiceShareDto
{
    /// <summary>The public invoice URL, for the plain "copy link" action.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>The composed message, shown so the owner can read it before sending.</summary>
    public string Message { get; init; } = string.Empty;

    public string EmailSubject { get; init; } = string.Empty;

    /// <summary>Addressed to the customer when a usable number is on file; unaddressed otherwise.</summary>
    public string WhatsAppUrl { get; init; } = string.Empty;

    /// <summary>Addressed to the customer when an email is on file; unaddressed otherwise.</summary>
    public string MailtoUrl { get; init; } = string.Empty;

    /// <summary>Digits only, or null when the snapshot holds no usable number. For display.</summary>
    public string? CustomerPhone { get; init; }

    public string? CustomerEmail { get; init; }
}
