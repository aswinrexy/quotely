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

    /// <summary>
    /// The idempotency key: the connection this arrived through, then the provider's own event
    /// identifier (Razorpay sends it as x-razorpay-event-id).
    ///
    /// Scoped per connection rather than global, because two merchants' Razorpay accounts number
    /// their events independently. A global key would let one merchant's event id collide with
    /// another's and silently discard a real payment as a "duplicate".
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    /// <summary>Which merchant connection the delivery was addressed to. Null for platform events.</summary>
    public Guid? MerchantConnectionId { get; set; }

    /// <summary>
    /// The tenant, denormalised. Lets the admin view answer "what has this business received
    /// lately?" without joining through the connection — and keeps the answer correct after a
    /// business reconnects and gets a new connection row.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>Safe correlation identifiers only — never the payload.</summary>
    public string? ProviderOrderId { get; set; }
    public string? ProviderPaymentId { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}
