using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.Models;
using Quotely.Api.Payments;

namespace Quotely.Api.Billing;

public interface ISubscriptionWebhookService
{
    /// <summary>Returns true when the event was accepted, including an already-seen duplicate.</summary>
    Task<bool> HandleAsync(string rawBody, string? signature, string? eventId, CancellationToken ct = default);
}

/// <summary>
/// Quotely's OWN subscription webhooks, on a path of their own.
///
/// Kept entirely separate from <c>WebhookService</c>, which handles merchant invoice payments.
/// They verify against different secrets, resolve different entities and change different money,
/// and merging them into one endpoint with a branch would mean one bug could let a merchant's
/// signed delivery move a subscription — or the reverse.
///
/// What they do share is the WebhookEvents table and its unique (Provider, EventId) index. That
/// is deliberate: idempotency is the same problem in both cases, and the provider discriminator
/// keeps the two sets of keys from ever meeting.
/// </summary>
public class SubscriptionWebhookService : ISubscriptionWebhookService
{
    /// <summary>
    /// The provider discriminator for billing events. Distinct from plain "Razorpay" so a
    /// merchant event id and a billing event id cannot collide in the idempotency index, even
    /// though both come from Razorpay.
    /// </summary>
    public const string BillingProvider = "Razorpay:Billing";

    private readonly AppDbContext _db;
    private readonly ISaasBillingProvider _provider;
    private readonly ISubscriptionService _subscriptions;
    private readonly ILogger<SubscriptionWebhookService> _logger;

    public SubscriptionWebhookService(
        AppDbContext db,
        ISaasBillingProvider provider,
        ISubscriptionService subscriptions,
        ILogger<SubscriptionWebhookService> logger)
    {
        _db = db;
        _provider = provider;
        _subscriptions = subscriptions;
        _logger = logger;
    }

    public async Task<bool> HandleAsync(
        string rawBody, string? signature, string? eventId, CancellationToken ct = default)
    {
        SubscriptionNotification notification;
        try
        {
            notification = _provider.ParseWebhook(rawBody, signature, eventId);
        }
        catch (PaymentSignatureException ex)
        {
            // Never echo why. An attacker probing the endpoint learns nothing beyond "no".
            _logger.LogWarning("Rejected billing webhook: {Reason}", ex.Message);
            return false;
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning("Rejected billing webhook: {Reason}", ex.Message);
            return false;
        }

        _logger.LogInformation(
            "Billing webhook {EventType} received (event {EventId})",
            notification.EventType, notification.EventId);

        var record = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            Provider = BillingProvider,
            EventId = notification.EventId,
            EventType = notification.EventType,
            ProviderOrderId = notification.Subscription?.Id
        };

        _db.WebhookEvents.Add(record);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            _logger.LogInformation(
                "Billing event {EventId} was already processed; acknowledging", notification.EventId);
            return true;
        }

        if (notification.Subscription is null)
        {
            _logger.LogInformation("Billing webhook {EventType} needs no action", notification.EventType);
            await MarkProcessedAsync(record, ct);
            return true;
        }

        // One route into the subscription state machine, shared with confirmation and
        // cancellation. A webhook cannot express a transition the other paths cannot.
        await _subscriptions.ApplyProviderStateAsync(
            notification.Subscription, notification.Charged, notification.PaymentFailed, ct);

        await MarkProcessedAsync(record, ct);
        return true;
    }

    private async Task MarkProcessedAsync(WebhookEvent record, CancellationToken ct)
    {
        var tracked = await _db.WebhookEvents.FirstOrDefaultAsync(e => e.Id == record.Id, ct);
        if (tracked is null) return;

        tracked.ProcessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
