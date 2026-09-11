using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Quotely.Api.Models;

namespace Quotely.Api.Payments;

/// <summary>
/// Razorpay adapter. Everything Razorpay-shaped lives here: the REST calls, the HMAC signature
/// schemes, and the mapping from Razorpay's status vocabulary onto ours. The rest of the
/// application sees only the models in PaymentModels.cs.
///
/// No SDK is used. Razorpay's maintained .NET package is stale, and this integration is one
/// POST, one GET and two HMAC verifications — not worth a dependency.
/// </summary>
public class RazorpayPaymentProvider : IPaymentProvider
{
    private readonly HttpClient _http;
    private readonly RazorpayOptions _options;
    private readonly ILogger<RazorpayPaymentProvider> _logger;

    public RazorpayPaymentProvider(
        HttpClient http,
        IOptions<RazorpayOptions> options,
        ILogger<RazorpayPaymentProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = http;
        _http.BaseAddress = new Uri(_options.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        if (_options.IsConfigured)
        {
            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }
    }

    public string Name => PaymentProviders.Razorpay;
    public string PublicKey => _options.KeyId;
    public bool IsConfigured => _options.IsConfigured;

    // ---- money ---------------------------------------------------------

    /// <summary>
    /// ₹1,250.50 → 125050 paise. Pure decimal arithmetic: scaling a binary float by 100 is the
    /// classic way to be one paise wrong, and this is money.
    /// </summary>
    public long ToMinorUnits(decimal amount)
    {
        if (amount < 0) throw new PaymentProviderException("A payment amount cannot be negative.");

        var minor = decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (minor > long.MaxValue) throw new PaymentProviderException("The payment amount is too large.");

        return (long)minor;
    }

    // ---- orders --------------------------------------------------------

    public async Task<ProviderOrder> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        EnsureConfigured();

        var minorUnits = ToMinorUnits(request.Amount);
        if (minorUnits <= 0)
            throw new PaymentProviderException("There is nothing left to pay on this invoice.");

        var payload = new Dictionary<string, object?>
        {
            ["amount"] = minorUnits,
            ["currency"] = request.Currency,
            // Our payment row id, so a Razorpay dashboard entry can be traced back to us.
            ["receipt"] = request.PaymentId.ToString("N"),
            // Razorpay's own idempotency handle: the same receipt value does not create a
            // duplicate order when a retried request arrives.
            ["notes"] = new Dictionary<string, string>
            {
                ["invoiceNumber"] = request.InvoiceNumber,
                ["paymentId"] = request.PaymentId.ToString("N")
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "orders", payload, ct);
        using var document = await ReadJsonAsync(response, "order creation", ct);
        var root = document.RootElement;

        var orderId = root.GetProperty("id").GetString()
                      ?? throw new PaymentProviderException("The payment provider returned an order without an id.");

        return new ProviderOrder(
            orderId,
            root.TryGetProperty("amount", out var amount) ? amount.GetInt64() : minorUnits,
            root.TryGetProperty("currency", out var currency) ? currency.GetString() ?? request.Currency : request.Currency);
    }

    public async Task<PaymentOutcome> GetPaymentAsync(string providerPaymentId, CancellationToken ct = default)
    {
        EnsureConfigured();

        using var response = await SendAsync(HttpMethod.Get, $"payments/{Uri.EscapeDataString(providerPaymentId)}", null, ct);
        using var document = await ReadJsonAsync(response, "payment lookup", ct);

        return MapPayment(document.RootElement);
    }

    // ---- signatures ----------------------------------------------------

    /// <summary>
    /// Razorpay signs "{order_id}|{payment_id}" with the key secret. Compared in fixed time so
    /// the check cannot be probed a byte at a time.
    /// </summary>
    public void VerifyCheckoutSignature(CheckoutResult result)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(result.ProviderOrderId) ||
            string.IsNullOrWhiteSpace(result.ProviderPaymentId) ||
            string.IsNullOrWhiteSpace(result.Signature))
            throw new PaymentSignatureException("The payment confirmation was incomplete.");

        var expected = HexHmac($"{result.ProviderOrderId}|{result.ProviderPaymentId}", _options.KeySecret);

        if (!FixedTimeEquals(expected, result.Signature))
            throw new PaymentSignatureException("The payment signature did not match.");
    }

    /// <summary>
    /// Webhook signatures are computed over the RAW body. Deserialising and re-serialising first
    /// changes whitespace and key order and would break every verification, so the caller reads
    /// the body as a string and hands it here untouched.
    /// </summary>
    public WebhookNotification ParseWebhook(string rawBody, string? signatureHeader, string? eventIdHeader)
    {
        if (!_options.WebhooksConfigured)
            throw new PaymentProviderException("Webhooks are not configured.");

        if (string.IsNullOrWhiteSpace(signatureHeader))
            throw new PaymentSignatureException("The webhook signature header was missing.");

        var expected = HexHmac(rawBody, _options.WebhookSecret);
        if (!FixedTimeEquals(expected, signatureHeader))
            throw new PaymentSignatureException("The webhook signature did not match.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            throw new PaymentProviderException("The webhook body was not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            var eventType = root.TryGetProperty("event", out var evt) ? evt.GetString() ?? string.Empty : string.Empty;

            // Razorpay sends x-razorpay-event-id; fall back to a deterministic hash of the body so
            // a delivery without the header still cannot be processed twice.
            var eventId = !string.IsNullOrWhiteSpace(eventIdHeader)
                ? eventIdHeader
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();

            return new WebhookNotification
            {
                EventId = eventId,
                EventType = eventType,
                Outcome = ExtractOutcome(root, eventType)
            };
        }
    }

    // ---- razorpay → domain mapping -------------------------------------

    /// <summary>
    /// Pulls the payment entity out of whichever envelope the event uses. order.paid carries both
    /// an order and a payment; the payment.* events carry only a payment.
    /// </summary>
    private static PaymentOutcome? ExtractOutcome(JsonElement root, string eventType)
    {
        if (eventType is not ("payment.authorized" or "payment.captured" or "payment.failed" or "order.paid"))
            return null;

        if (!root.TryGetProperty("payload", out var payload)) return null;
        if (!payload.TryGetProperty("payment", out var paymentWrapper)) return null;
        if (!paymentWrapper.TryGetProperty("entity", out var entity)) return null;

        return MapPayment(entity);
    }

    /// <summary>
    /// Razorpay's status strings become our own here — the one place that translation happens.
    /// "authorized" is deliberately Pending: the money is held, not captured, and must not count
    /// towards an invoice's paid total.
    /// </summary>
    private static PaymentOutcome MapPayment(JsonElement entity)
    {
        var providerStatus = entity.TryGetProperty("status", out var status) ? status.GetString() : null;

        var mapped = providerStatus switch
        {
            "captured" => PaymentStatus.Captured,
            "authorized" => PaymentStatus.Pending,
            "created" => PaymentStatus.Created,
            "failed" => PaymentStatus.Failed,
            "refunded" => PaymentStatus.Captured, // still a real capture; refunds are out of scope
            _ => PaymentStatus.Pending
        };

        DateTime? paidAt = null;
        if (mapped == PaymentStatus.Captured && entity.TryGetProperty("created_at", out var createdAt) &&
            createdAt.ValueKind == JsonValueKind.Number)
        {
            paidAt = DateTimeOffset.FromUnixTimeSeconds(createdAt.GetInt64()).UtcDateTime;
        }

        return new PaymentOutcome
        {
            ProviderOrderId = entity.TryGetProperty("order_id", out var orderId) ? orderId.GetString() ?? string.Empty : string.Empty,
            ProviderPaymentId = entity.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Status = mapped,
            AmountInMinorUnits = entity.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number
                ? amount.GetInt64()
                : 0,
            Currency = entity.TryGetProperty("currency", out var currency) ? currency.GetString() ?? "INR" : "INR",
            Method = entity.TryGetProperty("method", out var method) ? method.GetString() : null,
            FailureReason = entity.TryGetProperty("error_description", out var reason) ? reason.GetString() : null,
            PaidAt = paidAt
        };
    }

    // ---- plumbing ------------------------------------------------------

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
            throw new PaymentProviderException("Online payments are not configured for this deployment.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        try
        {
            return await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Razorpay request to {Path} timed out", path);
            throw new PaymentProviderException("The payment provider did not respond in time. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Razorpay request to {Path} failed", path);
            throw new PaymentProviderException("The payment provider could not be reached. Please try again.");
        }
    }

    /// <summary>
    /// Razorpay's own error text is logged but never returned: it can name internal accounts and
    /// is not written for customers.
    /// </summary>
    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Razorpay {Operation} returned {Status}: {Body}",
                operation, (int)response.StatusCode, Truncate(content));
            throw new PaymentProviderException("The payment could not be set up. Please try again.");
        }

        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            throw new PaymentProviderException("The payment provider returned an unexpected response.");
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];

    private static string HexHmac(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(actual.Trim().ToLower(CultureInfo.InvariantCulture));
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
