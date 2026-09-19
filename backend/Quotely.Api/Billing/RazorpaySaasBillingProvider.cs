using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Quotely.Api.Models;
using Quotely.Api.Payments;

namespace Quotely.Api.Billing;

/// <summary>
/// Razorpay Subscriptions, on QUOTELY'S OWN account.
///
/// Every event name, endpoint and status string here is taken from Razorpay's published
/// documentation and is recorded in docs/razorpay-partner.md with the page it came from. None of
/// it is guessed.
///
/// The credentials are Quotely's, set once in the constructor — which is safe here precisely
/// because there is only ever one account involved. That is the opposite of the merchant adapter,
/// where a constructor-set credential would be a cross-tenant leak.
/// </summary>
public class RazorpaySaasBillingProvider : ISaasBillingProvider
{
    private readonly HttpClient _http;
    private readonly RazorpayOptions _options;
    private readonly ILogger<RazorpaySaasBillingProvider> _logger;

    public RazorpaySaasBillingProvider(
        HttpClient http,
        IOptions<RazorpayOptions> options,
        ILogger<RazorpaySaasBillingProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = http;
        _http.BaseAddress = new Uri(_options.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        if (_options.IsConfigured)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}")));
        }
    }

    public string Name => PaymentProviders.Razorpay;
    public bool IsConfigured => _options.IsConfigured;
    public string PublicKey => _options.KeyId;

    // ---- plans -----------------------------------------------------------

    public async Task<string> EnsurePlanAsync(BillingPlanOptions plan, CancellationToken ct = default)
    {
        EnsureConfigured();

        var payload = new Dictionary<string, object?>
        {
            ["period"] = plan.Interval,
            ["interval"] = plan.IntervalCount,
            ["item"] = new Dictionary<string, object?>
            {
                ["name"] = plan.Name,
                ["description"] = plan.Description,
                // Paise, by the same exact decimal arithmetic the invoice side uses.
                ["amount"] = ToMinorUnits(plan.Price),
                ["currency"] = plan.Currency
            },
            // Our own plan code, so a plan in the Razorpay dashboard can be traced back here.
            ["notes"] = new Dictionary<string, string> { ["quotelyPlanCode"] = plan.Code }
        };

        using var response = await SendAsync(HttpMethod.Post, "plans", payload, ct);
        using var document = await ReadJsonAsync(response, "plan creation", ct);

        return document.RootElement.GetProperty("id").GetString()
               ?? throw new PaymentProviderException("Razorpay returned a plan without an id.");
    }

    // ---- subscriptions ---------------------------------------------------

    public async Task<ProviderSubscription> CreateSubscriptionAsync(
        string providerPlanId,
        int? totalCycles,
        DateTime? startAt,
        IReadOnlyDictionary<string, string> notes,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        var payload = new Dictionary<string, object?>
        {
            ["plan_id"] = providerPlanId,
            // Razorpay requires a cycle count. A long one rather than an unbounded subscription:
            // the business can cancel at any time, and a finite mandate is easier for a customer
            // to agree to than an indefinite one.
            ["total_count"] = totalCycles ?? 120,
            ["customer_notify"] = 1,
            ["notes"] = notes.ToDictionary(kv => kv.Key, kv => kv.Value)
        };

        if (startAt is not null)
        {
            // THE free-trial mechanism. Razorpay begins charging at start_at, so a subscription
            // created today with a start date six months out is six free months followed by the
            // normal monthly charge. Documented behaviour — see docs/razorpay-partner.md.
            payload["start_at"] = new DateTimeOffset(
                DateTime.SpecifyKind(startAt.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
        }

        using var response = await SendAsync(HttpMethod.Post, "subscriptions", payload, ct);
        using var document = await ReadJsonAsync(response, "subscription creation", ct);

        return MapSubscription(document.RootElement);
    }

    public async Task<ProviderSubscription> GetSubscriptionAsync(
        string providerSubscriptionId, CancellationToken ct = default)
    {
        EnsureConfigured();

        using var response = await SendAsync(
            HttpMethod.Get, $"subscriptions/{Uri.EscapeDataString(providerSubscriptionId)}", null, ct);
        using var document = await ReadJsonAsync(response, "subscription lookup", ct);

        return MapSubscription(document.RootElement);
    }

    public async Task<ProviderSubscription> CancelSubscriptionAsync(
        string providerSubscriptionId, bool atCycleEnd, CancellationToken ct = default)
    {
        EnsureConfigured();

        using var response = await SendAsync(
            HttpMethod.Post,
            $"subscriptions/{Uri.EscapeDataString(providerSubscriptionId)}/cancel",
            new Dictionary<string, object?> { ["cancel_at_cycle_end"] = atCycleEnd ? 1 : 0 },
            ct);

        using var document = await ReadJsonAsync(response, "subscription cancellation", ct);
        return MapSubscription(document.RootElement);
    }

    // ---- signatures ------------------------------------------------------

    /// <summary>
    /// Razorpay signs the subscription checkout response over "{payment_id}|{subscription_id}".
    ///
    /// NOTE THE ORDER — it is the REVERSE of the invoice signature, which is "{order_id}|{payment_id}".
    /// Razorpay documents both, and they genuinely differ: the payment comes first for a
    /// subscription and second for an order. Getting it backwards yields a check that rejects
    /// every legitimate mandate, which is why this is written out here rather than delegated to a
    /// shared two-string helper that would hide the difference.
    /// </summary>
    public void VerifySubscriptionSignature(
        string providerSubscriptionId, string providerPaymentId, string signature)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(providerSubscriptionId) ||
            string.IsNullOrWhiteSpace(providerPaymentId) ||
            string.IsNullOrWhiteSpace(signature))
            throw new PaymentSignatureException("The subscription confirmation was incomplete.");

        var expected = HexHmac($"{providerPaymentId}|{providerSubscriptionId}", _options.KeySecret);

        if (!FixedTimeEquals(expected, signature))
            throw new PaymentSignatureException("The subscription signature did not match.");
    }

    public SubscriptionNotification ParseWebhook(
        string rawBody, string? signatureHeader, string? eventIdHeader)
    {
        if (!_options.WebhooksConfigured)
            throw new PaymentProviderException("Subscription webhooks are not configured.");

        if (string.IsNullOrWhiteSpace(signatureHeader))
            throw new PaymentSignatureException("The webhook signature header was missing.");

        // Quotely's own webhook secret. A merchant's secret cannot verify here, which is the
        // point: a business that connected its own Razorpay must not be able to forge events
        // about its own subscription.
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

            var eventId = !string.IsNullOrWhiteSpace(eventIdHeader)
                ? eventIdHeader
                : "body:" + Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes($"{eventType}\n{rawBody}"))).ToLowerInvariant();

            return new SubscriptionNotification
            {
                EventId = eventId,
                EventType = eventType,
                Subscription = ExtractSubscription(root, eventType),
                Charged = eventType == "subscription.charged",
                // Razorpay does not send a "subscription.payment_failed"; a failed charge is
                // reported as the subscription moving to pending, and then to halted once the
                // retries are exhausted. See docs/razorpay-partner.md.
                PaymentFailed = eventType is "subscription.pending" or "subscription.halted"
            };
        }
    }

    // ---- razorpay → domain ------------------------------------------------

    /// <summary>
    /// The subscription lifecycle events Razorpay publishes. Anything outside this set is parsed
    /// and acknowledged but carries no subscription, so an event we have never seen before cannot
    /// move an account's state by accident.
    /// </summary>
    private static readonly HashSet<string> HandledEvents = new(StringComparer.Ordinal)
    {
        "subscription.authenticated",
        "subscription.activated",
        "subscription.charged",
        "subscription.completed",
        "subscription.updated",
        "subscription.pending",
        "subscription.halted",
        "subscription.cancelled",
        "subscription.paused",
        "subscription.resumed"
    };

    private static ProviderSubscription? ExtractSubscription(JsonElement root, string eventType)
    {
        if (!HandledEvents.Contains(eventType)) return null;

        if (!root.TryGetProperty("payload", out var payload)) return null;
        if (!payload.TryGetProperty("subscription", out var wrapper)) return null;
        if (!wrapper.TryGetProperty("entity", out var entity)) return null;

        return MapSubscription(entity);
    }

    /// <summary>
    /// Razorpay's subscription statuses become ours here — the one place that translation
    /// happens.
    ///
    /// Two mappings deserve a note. "authenticated" means the mandate is signed but nothing has
    /// been charged yet, which during a trial is exactly Trialing. "halted" means every retry
    /// failed, which is the end of the road and therefore Expired rather than PastDue.
    /// </summary>
    private static ProviderSubscription MapSubscription(JsonElement entity)
    {
        var providerStatus = entity.TryGetProperty("status", out var status)
            ? status.GetString() ?? string.Empty
            : string.Empty;

        var mapped = providerStatus switch
        {
            "created" => SubscriptionStatus.Trialing,
            "authenticated" => SubscriptionStatus.Trialing,
            "active" => SubscriptionStatus.Active,
            "pending" => SubscriptionStatus.PastDue,
            "halted" => SubscriptionStatus.Expired,
            "cancelled" => SubscriptionStatus.Cancelled,
            "completed" => SubscriptionStatus.Expired,
            "expired" => SubscriptionStatus.Expired,
            // Paused is a state Quotely never asks for. Treated as past-due rather than as
            // expired: it is recoverable, and locking someone out of a recoverable state is worse
            // than letting them keep working while it is sorted out.
            "paused" => SubscriptionStatus.PastDue,
            _ => SubscriptionStatus.PastDue
        };

        return new ProviderSubscription
        {
            Id = entity.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Status = mapped,
            ProviderStatus = providerStatus,
            CurrentStart = ReadUnix(entity, "current_start"),
            CurrentEnd = ReadUnix(entity, "current_end"),
            ChargeAt = ReadUnix(entity, "charge_at"),
            EndedAt = ReadUnix(entity, "ended_at"),
            ShortUrl = entity.TryGetProperty("short_url", out var url) ? url.GetString() : null
        };
    }

    private static DateTime? ReadUnix(JsonElement entity, string name) =>
        entity.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(value.GetInt64()).UtcDateTime
            : null;

    // ---- plumbing ---------------------------------------------------------

    private static long ToMinorUnits(decimal amount)
    {
        if (amount < 0) throw new PaymentProviderException("A price cannot be negative.");
        return (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
            throw new PaymentProviderException("Subscription billing is not configured for this deployment.");
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
            _logger.LogWarning("Razorpay billing request to {Path} timed out", path);
            throw new PaymentProviderException("Razorpay did not respond in time. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Razorpay billing request to {Path} failed", path);
            throw new PaymentProviderException("Razorpay could not be reached. Please try again.");
        }
    }

    private async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response, string operation, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Razorpay {Operation} returned {Status}: {Body}",
                operation, (int)response.StatusCode, content.Length <= 500 ? content : content[..500]);
            throw new PaymentProviderException("Your subscription could not be set up. Please try again.");
        }

        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            throw new PaymentProviderException("Razorpay returned an unexpected response.");
        }
    }

    private static string HexHmac(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(actual.Trim().ToLower(CultureInfo.InvariantCulture));
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
