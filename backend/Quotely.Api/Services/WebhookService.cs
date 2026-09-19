using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.Models;
using Quotely.Api.Payments;

namespace Quotely.Api.Services;

public interface IWebhookService
{
    /// <summary>
    /// Handles a merchant payment webhook delivered to one connection's own URL.
    ///
    /// Returns true when the event was accepted — which includes a duplicate we have already
    /// processed, because the provider should stop retrying it. False means the delivery was
    /// rejected and nothing was touched.
    /// </summary>
    Task<bool> HandleMerchantAsync(
        string routeToken, string rawBody, string? signature, string? eventId, CancellationToken ct = default);
}

/// <summary>
/// Verifies and records merchant payment webhooks, then hands the outcome to
/// <see cref="IPaymentService"/>. It contains no financial arithmetic of its own: checkout
/// verification and webhooks must not become two payment state machines that can disagree.
///
/// THE ORDER OF OPERATIONS HERE IS THE SECURITY MODEL, and it is deliberately this and not
/// anything more convenient:
///
///   1. The URL's route token selects a connection. The token is unguessable and belongs to
///      exactly one merchant.
///   2. That connection's own secret verifies the signature. A body signed with Business A's
///      secret cannot verify against Business B's, so a delivery cannot be replayed at another
///      merchant's URL.
///   3. Only then is the body parsed, and only then may a payment be touched — and the payment
///      must belong to the same tenant, which <see cref="IPaymentService.ProcessOutcomeAsync"/>
///      checks again on its own.
///
/// What is never done: reading account_id (or anything else) out of the unverified body to decide
/// whose money this is. The payload is attacker-controlled until step 2 has passed.
/// </summary>
public class WebhookService : IWebhookService
{
    private readonly AppDbContext _db;
    private readonly IMerchantPaymentProvider _provider;
    private readonly IMerchantConnectionService _merchants;
    private readonly IPaymentService _payments;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(
        AppDbContext db,
        IMerchantPaymentProvider provider,
        IMerchantConnectionService merchants,
        IPaymentService payments,
        ILogger<WebhookService> logger)
    {
        _db = db;
        _provider = provider;
        _merchants = merchants;
        _payments = payments;
        _logger = logger;
    }

    public async Task<bool> HandleMerchantAsync(
        string routeToken, string rawBody, string? signature, string? eventId, CancellationToken ct = default)
    {
        // ---- 1. which connection was this addressed to? ----
        if (!PublicTokenGenerator.LooksValid(routeToken))
        {
            _logger.LogWarning("Rejected webhook: the delivery address was malformed");
            return false;
        }

        var connection = await _merchants.FindByWebhookRouteAsync(routeToken, ct);

        if (connection is null)
        {
            // Terse on purpose. Distinguishing "no such connection" from "bad signature" would
            // let someone enumerate which route tokens exist.
            _logger.LogWarning("Rejected webhook: no connection matched the delivery address");
            return false;
        }

        var secret = _merchants.ReadWebhookSecret(connection);
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning(
                "Rejected webhook for merchant {UserId}: the connection holds no webhook secret",
                connection.UserId);
            return false;
        }

        // ---- 2. is it genuinely from this merchant's account? ----
        WebhookNotification notification;
        try
        {
            notification = _provider.ParseWebhook(secret, rawBody, signature, eventId);
        }
        catch (PaymentSignatureException ex)
        {
            // Never echo why. An attacker probing the endpoint learns nothing beyond "no".
            _logger.LogWarning("Rejected webhook for merchant {UserId}: {Reason}", connection.UserId, ex.Message);
            return false;
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning("Rejected webhook for merchant {UserId}: {Reason}", connection.UserId, ex.Message);
            return false;
        }

        // The signature has passed, so the payload is now trustworthy enough to cross-check. If
        // Razorpay names an account, it must be the one this connection is for — a mismatch means
        // two connections share a secret, which should be impossible and is worth refusing over.
        if (!string.IsNullOrWhiteSpace(notification.ProviderAccountId) &&
            !string.IsNullOrWhiteSpace(connection.ProviderAccountId) &&
            !string.Equals(notification.ProviderAccountId, connection.ProviderAccountId, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Rejected webhook for merchant {UserId}: the event names a different Razorpay account",
                connection.UserId);
            return false;
        }

        _logger.LogInformation(
            "Webhook {EventType} received for merchant {UserId} (event {EventId})",
            notification.EventType, connection.UserId, notification.EventId);

        // ---- 3. exactly once ----
        // The unique index on (Provider, EventId) is the idempotency guarantee. A retried
        // delivery loses this insert and is acknowledged without touching a single total again.
        //
        // The key is scoped per connection, because two merchants' Razorpay accounts number their
        // events independently and a collision between them would silently drop a real payment.
        var record = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            Provider = PaymentProviders.Razorpay,
            EventId = $"{connection.Id:N}:{notification.EventId}",
            EventType = notification.EventType,
            MerchantConnectionId = connection.Id,
            UserId = connection.UserId,
            ProviderOrderId = notification.Outcome?.ProviderOrderId,
            ProviderPaymentId = notification.Outcome?.ProviderPaymentId
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
                "Webhook event {EventId} was already processed; acknowledging", notification.EventId);
            return true;
        }

        if (notification.Outcome is null)
        {
            // A recognised event we take no action on, or one we do not handle at all.
            _logger.LogInformation("Webhook {EventType} needs no action", notification.EventType);
            await MarkProcessedAsync(record, ct);
            return true;
        }

        // ---- 4. apply it, as this merchant ----
        var merchant = await _merchants.TryResolveAsync(connection.UserId, ct);

        if (merchant is null)
        {
            // The connection stopped being usable between the delivery and now — disconnected,
            // expired, or pointed at the wrong environment. Acknowledged rather than retried:
            // the provider redelivering will not make the credentials work again.
            _logger.LogWarning(
                "Webhook {EventId} for merchant {UserId} was not applied: the connection is no longer usable",
                notification.EventId, connection.UserId);
            await MarkProcessedAsync(record, ct);
            return true;
        }

        try
        {
            // The same call the checkout verification path makes. One route into the ledger.
            await _payments.ProcessOutcomeAsync(notification.Outcome, merchant, ct);
        }
        catch (Middleware.ApiException ex)
        {
            // A payment we cannot attribute — an order that is not ours, a mismatched amount, or
            // one belonging to a different tenant. Acknowledged so the provider stops retrying
            // something we will never accept.
            _logger.LogWarning(
                "Webhook {EventId} for payment {ProviderPaymentId} was not applied: {Reason}",
                notification.EventId, notification.Outcome.ProviderPaymentId, ex.Message);
            await MarkProcessedAsync(record, ct);
            return true;
        }

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
