using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Quotely.Api.Models;
using Quotely.Api.Payments;

namespace Quotely.Tests;

/// <summary>
/// Stands in for Razorpay so the suite runs offline. It uses the same HMAC-SHA256 signature
/// schemes as the real adapter, so signature tests exercise real cryptography rather than a
/// boolean flag — only the network calls are replaced.
/// </summary>
public class FakePaymentProvider : IPaymentProvider
{
    public const string TestKeyId = "rzp_test_fake_key";
    public const string TestKeySecret = "fake_key_secret_for_tests";
    public const string TestWebhookSecret = "fake_webhook_secret_for_tests";

    private int _orderCounter;

    public string Name => PaymentProviders.Razorpay;
    public string PublicKey => TestKeyId;
    public bool IsConfigured { get; set; } = true;

    /// <summary>Set by a test to make the next order creation fail, as a provider outage would.</summary>
    public bool FailOrderCreation { get; set; }

    /// <summary>Orders this provider has issued, so tests can assert on what the server asked for.</summary>
    public ConcurrentDictionary<string, (long Amount, string Currency)> Orders { get; } = new();

    /// <summary>What GetPaymentAsync should report for a given payment id.</summary>
    public ConcurrentDictionary<string, PaymentOutcome> Payments { get; } = new();

    public long ToMinorUnits(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    public Task<ProviderOrder> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        if (FailOrderCreation)
            throw new PaymentProviderException("The payment could not be set up. Please try again.");

        var minor = ToMinorUnits(request.Amount);
        var orderId = $"order_test_{Interlocked.Increment(ref _orderCounter):D6}";
        Orders[orderId] = (minor, request.Currency);

        return Task.FromResult(new ProviderOrder(orderId, minor, request.Currency));
    }

    public void VerifyCheckoutSignature(CheckoutResult result)
    {
        var expected = Sign($"{result.ProviderOrderId}|{result.ProviderPaymentId}", TestKeySecret);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(result.Signature)))
            throw new PaymentSignatureException("The payment signature did not match.");
    }

    public Task<PaymentOutcome> GetPaymentAsync(string providerPaymentId, CancellationToken ct = default)
    {
        if (!Payments.TryGetValue(providerPaymentId, out var outcome))
            throw new PaymentProviderException("The payment could not be found.");

        return Task.FromResult(outcome);
    }

    public WebhookNotification ParseWebhook(string rawBody, string? signatureHeader, string? eventIdHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            throw new PaymentSignatureException("The webhook signature header was missing.");

        var expected = Sign(rawBody, TestWebhookSecret);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signatureHeader)))
            throw new PaymentSignatureException("The webhook signature did not match.");

        using var document = System.Text.Json.JsonDocument.Parse(rawBody);
        var root = document.RootElement;
        var eventType = root.GetProperty("event").GetString() ?? string.Empty;

        var eventId = string.IsNullOrWhiteSpace(eventIdHeader)
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant()
            : eventIdHeader;

        PaymentOutcome? outcome = null;
        if (root.TryGetProperty("payload", out var payload) &&
            payload.TryGetProperty("payment", out var wrapper) &&
            wrapper.TryGetProperty("entity", out var entity))
        {
            outcome = new PaymentOutcome
            {
                ProviderOrderId = entity.GetProperty("order_id").GetString()!,
                ProviderPaymentId = entity.GetProperty("id").GetString()!,
                Status = entity.GetProperty("status").GetString() switch
                {
                    "captured" => PaymentStatus.Captured,
                    "authorized" => PaymentStatus.Pending,
                    "failed" => PaymentStatus.Failed,
                    _ => PaymentStatus.Pending
                },
                AmountInMinorUnits = entity.GetProperty("amount").GetInt64(),
                Currency = entity.GetProperty("currency").GetString() ?? "INR",
                Method = entity.TryGetProperty("method", out var m) ? m.GetString() : null,
                FailureReason = entity.TryGetProperty("error_description", out var e) ? e.GetString() : null
            };
        }

        return new WebhookNotification { EventId = eventId, EventType = eventType, Outcome = outcome };
    }

    // ---- helpers used by the tests -------------------------------------

    public static string Sign(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static string CheckoutSignature(string orderId, string paymentId) =>
        Sign($"{orderId}|{paymentId}", TestKeySecret);

    public static string WebhookSignature(string rawBody) => Sign(rawBody, TestWebhookSecret);

    /// <summary>Registers what the provider will report for a payment, as Razorpay would.</summary>
    public void Arrange(string paymentId, string orderId, PaymentStatus status, long amountMinor,
        string currency = "INR", string? method = "upi", string? failureReason = null)
    {
        Payments[paymentId] = new PaymentOutcome
        {
            ProviderOrderId = orderId,
            ProviderPaymentId = paymentId,
            Status = status,
            AmountInMinorUnits = amountMinor,
            Currency = currency,
            Method = method,
            FailureReason = failureReason,
            PaidAt = status == PaymentStatus.Captured ? DateTime.UtcNow : null
        };
    }

    public static string WebhookBody(string eventType, string paymentId, string orderId, string status,
        long amountMinor, string currency = "INR", string method = "upi")
    {
        var entity = string.Join(",",
            $"\"id\":\"{paymentId}\"",
            $"\"order_id\":\"{orderId}\"",
            $"\"status\":\"{status}\"",
            $"\"amount\":{amountMinor}",
            $"\"currency\":\"{currency}\"",
            $"\"method\":\"{method}\"");

        return $"{{\"event\":\"{eventType}\",\"payload\":{{\"payment\":{{\"entity\":{{{entity}}}}}}}}}";
    }
}
