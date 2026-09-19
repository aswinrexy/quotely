using Quotely.Api.Models;

namespace Quotely.Api.Payments;

/// <summary>
/// Provider-neutral models. The invoice and payment domain speaks only these; nothing in
/// Quotely.Api.Services or the controllers knows what a Razorpay response looks like.
/// </summary>

/// <summary>What we ask a provider to collect. The amount is always decided by the server.</summary>
public record CreateOrderRequest(
    decimal Amount,
    string Currency,
    string InvoiceNumber,
    /// <summary>Our own correlation id, echoed back by the provider where supported.</summary>
    Guid PaymentId);

/// <summary>The provider's acknowledgement that an order exists.</summary>
public record ProviderOrder(string OrderId, long AmountInMinorUnits, string Currency);

/// <summary>
/// What the browser needs to open the provider's checkout. Contains the publishable key only —
/// the secret never leaves the server.
/// </summary>
public record CheckoutConfiguration(
    string KeyId,
    string OrderId,
    long Amount,
    string Currency,
    string InvoiceNumber,
    string BusinessName,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerContact);

/// <summary>What the customer's browser hands back after checkout. Every field is untrusted.</summary>
public record CheckoutResult(string ProviderOrderId, string ProviderPaymentId, string Signature);

/// <summary>
/// A provider event translated into our vocabulary. Produced by checkout verification and by
/// webhook parsing alike, so both paths feed the same domain service.
/// </summary>
public record PaymentOutcome
{
    public required string ProviderOrderId { get; init; }
    public required string ProviderPaymentId { get; init; }
    public required PaymentStatus Status { get; init; }
    /// <summary>Amount the provider reports, in minor units, for cross-checking against our order.</summary>
    public required long AmountInMinorUnits { get; init; }
    public required string Currency { get; init; }
    public string? Method { get; init; }
    public string? FailureReason { get; init; }
    public DateTime? PaidAt { get; init; }
}

/// <summary>A verified webhook, reduced to the event identity plus its payment outcome.</summary>
public record WebhookNotification
{
    public required string EventId { get; init; }
    public required string EventType { get; init; }

    /// <summary>
    /// The sub-merchant account the provider says this event belongs to, when it says so.
    ///
    /// Used only to CROSS-CHECK the connection the delivery was addressed to. It never selects
    /// that connection: the payload is unauthenticated until the signature has been verified, and
    /// the signature can only be verified once a secret — and therefore a connection — has already
    /// been chosen by other means.
    /// </summary>
    public string? ProviderAccountId { get; init; }
    /// <summary>Null for events we recognise but do not act on.</summary>
    public PaymentOutcome? Outcome { get; init; }
}
