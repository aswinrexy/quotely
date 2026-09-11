namespace Quotely.Api.Models;

/// <summary>
/// A provider webhook we have already accepted. Providers retry deliveries, so the event id is
/// recorded under a unique index: a repeat delivery loses the insert race and is acknowledged
/// without touching any financial total a second time.
/// </summary>
public class WebhookEvent
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = PaymentProviders.Razorpay;

    /// <summary>The provider's event identifier (Razorpay sends it as x-razorpay-event-id).</summary>
    public string EventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    /// <summary>Safe correlation identifiers only — never the payload.</summary>
    public string? ProviderOrderId { get; set; }
    public string? ProviderPaymentId { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}
