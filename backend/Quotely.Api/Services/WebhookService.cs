using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.Models;
using Quotely.Api.Payments;

namespace Quotely.Api.Services;

public interface IWebhookService
{
    /// <summary>Returns true when the event was accepted (including an already-seen duplicate).</summary>
    Task<bool> HandleRazorpayAsync(string rawBody, string? signature, string? eventId, CancellationToken ct = default);
}

/// <summary>
/// Verifies and records provider webhooks, then hands the outcome to <see cref="IPaymentService"/>.
/// It contains no financial arithmetic of its own: checkout verification and webhooks must not
/// become two payment state machines that can disagree.
/// </summary>
public class WebhookService : IWebhookService
{
    private readonly AppDbContext _db;
    private readonly IPaymentProvider _provider;
    private readonly IPaymentService _payments;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(
        AppDbContext db,
        IPaymentProvider provider,
        IPaymentService payments,
        ILogger<WebhookService> logger)
    {
        _db = db;
        _provider = provider;
        _payments = payments;
        _logger = logger;
    }

    public async Task<bool> HandleRazorpayAsync(
        string rawBody, string? signature, string? eventId, CancellationToken ct = default)
    {
        WebhookNotification notification;
        try
        {
            notification = _provider.ParseWebhook(rawBody, signature, eventId);
        }
        catch (PaymentSignatureException ex)
        {
            // Never echo why. An attacker probing the endpoint learns nothing beyond "no".
            _logger.LogWarning("Rejected webhook: {Reason}", ex.Message);
            return false;
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning("Rejected webhook: {Reason}", ex.Message);
            return false;
        }

        _logger.LogInformation(
            "Webhook {EventType} received (event {EventId})", notification.EventType, notification.EventId);

        // The unique index on (Provider, EventId) is the idempotency guarantee. A retried
        // delivery loses this insert and is acknowledged without touching a single total again.
        var record = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            Provider = PaymentProviders.Razorpay,
            EventId = notification.EventId,
            EventType = notification.EventType,
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
            _logger.LogInformation("Webhook event {EventId} was already processed; acknowledging", notification.EventId);
            return true;
        }

        if (notification.Outcome is null)
        {
            // A recognised event we take no action on, or one we do not handle at all.
            _logger.LogInformation("Webhook {EventType} needs no action", notification.EventType);
            await MarkProcessedAsync(record, ct);
            return true;
        }

        try
        {
            // The same call the checkout verification path makes. One route into the ledger.
            await _payments.ProcessOutcomeAsync(notification.Outcome, ct);
        }
        catch (Middleware.ApiException ex)
        {
            // A payment we cannot attribute — an order that is not ours, a mismatched amount.
            // Acknowledged so the provider stops retrying something we will never accept.
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
