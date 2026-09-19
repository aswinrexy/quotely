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
///
/// Crucially, it is merchant-aware in the same way the real adapter is: every method takes a
/// <see cref="MerchantPaymentContext"/> and signs with the credentials on it. A test that
/// connects two businesses therefore gets two genuinely different signing keys, which is what
/// makes the cross-tenant tests mean something instead of asserting on a stub.
/// </summary>
public class FakePaymentProvider : IMerchantPaymentProvider
{
    public const string TestKeyId = "rzp_test_fake_key";
    public const string TestKeySecret = "fake_key_secret_for_tests";

    /// <summary>A second business's credentials, for proving one cannot use the other's.</summary>
    public const string OtherKeyId = "rzp_test_other_key";
    public const string OtherKeySecret = "other_key_secret_for_tests";

    private int _orderCounter;

    public string Name => PaymentProviders.Razorpay;

    /// <summary>Set by a test to make the next order creation fail, as a provider outage would.</summary>
    public bool FailOrderCreation { get; set; }

    /// <summary>Set by a test to make credential checks fail, as a wrong key would.</summary>
    public bool RejectCredentials { get; set; }

    /// <summary>Orders this provider has issued, so tests can assert on what the server asked for.</summary>
    public ConcurrentDictionary<string, (long Amount, string Currency)> Orders { get; } = new();

    /// <summary>
    /// Which key each order was created with. This is the evidence for "Business A's invoice was
    /// collected into Business A's account" — an assertion that would be impossible to make
    /// against a provider that did not record whose credentials it was handed.
    /// </summary>
    public ConcurrentDictionary<string, string> OrderKeys { get; } = new();

    /// <summary>What GetPaymentAsync should report for a given payment id.</summary>
    public ConcurrentDictionary<string, PaymentOutcome> Payments { get; } = new();

    public long ToMinorUnits(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    public Task<ProviderOrder> CreateOrderAsync(
        MerchantPaymentContext context, CreateOrderRequest request, CancellationToken ct = default)
    {
        if (FailOrderCreation)
            throw new PaymentProviderException("The payment could not be set up. Please try again.");

        RequireCredentials(context);

        var minor = ToMinorUnits(request.Amount);
        var orderId = $"order_test_{Interlocked.Increment(ref _orderCounter):D6}";
        Orders[orderId] = (minor, request.Currency);
        OrderKeys[orderId] = context.Credentials.PublicKey;

        return Task.FromResult(new ProviderOrder(orderId, minor, request.Currency));
    }

    public CheckoutVerification VerifyCheckoutSignature(MerchantPaymentContext context, CheckoutResult result)
    {
        var secret = context.Credentials.CheckoutSigningSecret;
        if (string.IsNullOrWhiteSpace(secret)) return CheckoutVerification.NotVerifiable;

        var expected = Sign($"{result.ProviderOrderId}|{result.ProviderPaymentId}", secret);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(result.Signature)))
            throw new PaymentSignatureException("The payment signature did not match.");

        return CheckoutVerification.Verified;
    }

    public Task<PaymentOutcome> GetPaymentAsync(
        MerchantPaymentContext context, string providerPaymentId, CancellationToken ct = default)
    {
        RequireCredentials(context);

        if (!Payments.TryGetValue(providerPaymentId, out var outcome))
            throw new PaymentProviderException("The payment could not be found.");

        return Task.FromResult(outcome);
    }

    public Task<MerchantAccountProbe> ProbeAsync(MerchantPaymentContext context, CancellationToken ct = default)
    {
        if (RejectCredentials)
            throw new PaymentCredentialException(
                "Razorpay did not accept these credentials. Check the key and secret and try again.");

        RequireCredentials(context);
        return Task.FromResult(new MerchantAccountProbe(context.ProviderAccountId, null));
    }

    public WebhookNotification ParseWebhook(
        string webhookSecret, string rawBody, string? signatureHeader, string? eventIdHeader)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret))
            throw new PaymentProviderException("No webhook secret is configured for this account.");

        if (string.IsNullOrWhiteSpace(signatureHeader))
            throw new PaymentSignatureException("The webhook signature header was missing.");

        var expected = Sign(rawBody, webhookSecret);
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

        return new WebhookNotification
        {
            EventId = eventId,
            EventType = eventType,
            ProviderAccountId = root.TryGetProperty("account_id", out var account) ? account.GetString() : null,
            Outcome = outcome
        };
    }

    /// <summary>
    /// Mirrors the real adapter's refusal to act without credentials. Without this the fake would
    /// happily create an order for a merchant whose connection holds nothing, and the tests would
    /// pass on a code path that cannot work against Razorpay.
    /// </summary>
    private static void RequireCredentials(MerchantPaymentContext context)
    {
        var usable = context.Credentials.Mode switch
        {
            MerchantConnectionMode.KeyPair => !string.IsNullOrWhiteSpace(context.Credentials.KeySecret),
            MerchantConnectionMode.Oauth => !string.IsNullOrWhiteSpace(context.Credentials.AccessToken),
            _ => false
        };

        if (!usable)
            throw new PaymentCredentialException("This account has no usable Razorpay credentials.");
    }

    // ---- helpers used by the tests -------------------------------------

    public static string Sign(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static string CheckoutSignature(string orderId, string paymentId, string secret = TestKeySecret) =>
        Sign($"{orderId}|{paymentId}", secret);

    public static string WebhookSignature(string rawBody, string secret) => Sign(rawBody, secret);

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
        long amountMinor, string currency = "INR", string method = "upi", string? accountId = null)
    {
        var entity = string.Join(",",
            $"\"id\":\"{paymentId}\"",
            $"\"order_id\":\"{orderId}\"",
            $"\"status\":\"{status}\"",
            $"\"amount\":{amountMinor}",
            $"\"currency\":\"{currency}\"",
            $"\"method\":\"{method}\"");

        var account = accountId is null ? "" : $"\"account_id\":\"{accountId}\",";

        return $"{{{account}\"event\":\"{eventType}\",\"payload\":{{\"payment\":{{\"entity\":{{{entity}}}}}}}}}";
    }
}
