using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Quotely.Api.Models;

namespace Quotely.Api.Payments;

/// <summary>
/// Razorpay adapter for MERCHANT money — a customer paying a business that uses Quotely.
/// Everything Razorpay-shaped lives here: the REST calls, the HMAC signature schemes, and the
/// mapping from Razorpay's status vocabulary onto ours.
///
/// The one structural difference from V2.6: this class holds no credentials of its own. They
/// arrive per call on a <see cref="MerchantPaymentContext"/> and are attached to that one request.
/// A shared HttpClient with a constructor-set Authorization header — which is what this used to
/// be — is exactly how one tenant's payment ends up in another tenant's account, because the
/// header outlives the request that set it.
///
/// No SDK is used. Razorpay's maintained .NET package is stale, and this integration is a handful
/// of REST calls and two HMAC verifications.
/// </summary>
public class RazorpayMerchantPaymentProvider : IMerchantPaymentProvider
{
    private readonly HttpClient _http;
    private readonly RazorpayOptions _options;
    private readonly ILogger<RazorpayMerchantPaymentProvider> _logger;

    public RazorpayMerchantPaymentProvider(
        HttpClient http,
        IOptions<RazorpayOptions> options,
        ILogger<RazorpayMerchantPaymentProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = http;
        _http.BaseAddress = new Uri(_options.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        // Note what is NOT set here: DefaultRequestHeaders.Authorization. This client is shared by
        // every tenant, so a default credential on it would be a cross-tenant leak by construction.
    }

    public string Name => PaymentProviders.Razorpay;

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

    public async Task<ProviderOrder> CreateOrderAsync(
        MerchantPaymentContext context, CreateOrderRequest request, CancellationToken ct = default)
    {
        var minorUnits = ToMinorUnits(request.Amount);
        if (minorUnits <= 0)
            throw new PaymentProviderException("There is nothing left to pay on this invoice.");

        var payload = new Dictionary<string, object?>
        {
            ["amount"] = minorUnits,
            ["currency"] = request.Currency,
            // Our payment row id, so a Razorpay dashboard entry can be traced back to us.
            ["receipt"] = request.PaymentId.ToString("N"),
            ["notes"] = new Dictionary<string, string>
            {
                ["invoiceNumber"] = request.InvoiceNumber,
                ["paymentId"] = request.PaymentId.ToString("N")
            }
        };

        using var response = await SendAsync(context, HttpMethod.Post, "orders", payload, ct);
        using var document = await ReadJsonAsync(response, "order creation", ct);
        var root = document.RootElement;

        var orderId = root.GetProperty("id").GetString()
                      ?? throw new PaymentProviderException("The payment provider returned an order without an id.");

        return new ProviderOrder(
            orderId,
            root.TryGetProperty("amount", out var amount) ? amount.GetInt64() : minorUnits,
            root.TryGetProperty("currency", out var currency) ? currency.GetString() ?? request.Currency : request.Currency);
    }

    public async Task<PaymentOutcome> GetPaymentAsync(
        MerchantPaymentContext context, string providerPaymentId, CancellationToken ct = default)
    {
        using var response = await SendAsync(
            context, HttpMethod.Get, $"payments/{Uri.EscapeDataString(providerPaymentId)}", null, ct);
        using var document = await ReadJsonAsync(response, "payment lookup", ct);

        return MapPayment(document.RootElement);
    }

    /// <summary>
    /// Asks Razorpay for one payment, purely to find out whether the credentials are accepted. A
    /// cheap authenticated read is the only honest way to answer "do these work?" — inspecting the
    /// key's shape would accept a well-formed key that Razorpay has since revoked.
    /// </summary>
    public async Task<MerchantAccountProbe> ProbeAsync(
        MerchantPaymentContext context, CancellationToken ct = default)
    {
        using var response = await SendAsync(context, HttpMethod.Get, "payments?count=1", null, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new PaymentCredentialException(
                "Razorpay did not accept these credentials. Check the key and secret and try again.");

        using var document = await ReadJsonAsync(response, "credential check", ct);

        // A key pair identifies its account only by the key id itself; Razorpay does not return an
        // acc_ identifier on this endpoint. OAuth connections already know theirs from the token
        // exchange, so it is carried through rather than rediscovered.
        return new MerchantAccountProbe(context.ProviderAccountId, null);
    }

    // ---- signatures ----------------------------------------------------

    /// <summary>
    /// Razorpay signs "{order_id}|{payment_id}" with the merchant's key secret. Compared in fixed
    /// time so the check cannot be probed a byte at a time.
    ///
    /// Under OAuth we never receive that secret — that is the entire point of OAuth — so there is
    /// nothing to compute an expected signature from. This reports <see cref="CheckoutVerification.NotVerifiable"/>
    /// rather than passing or failing, and the caller confirms with Razorpay instead. Returning
    /// "verified" here would be the single worst lie this codebase could tell.
    /// </summary>
    public CheckoutVerification VerifyCheckoutSignature(MerchantPaymentContext context, CheckoutResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ProviderOrderId) ||
            string.IsNullOrWhiteSpace(result.ProviderPaymentId) ||
            string.IsNullOrWhiteSpace(result.Signature))
            throw new PaymentSignatureException("The payment confirmation was incomplete.");

        var secret = context.Credentials.CheckoutSigningSecret;
        if (string.IsNullOrWhiteSpace(secret))
            return CheckoutVerification.NotVerifiable;

        var expected = HexHmac($"{result.ProviderOrderId}|{result.ProviderPaymentId}", secret);

        if (!FixedTimeEquals(expected, result.Signature))
            throw new PaymentSignatureException("The payment signature did not match.");

        return CheckoutVerification.Verified;
    }

    /// <summary>
    /// Webhook signatures are computed over the RAW body. Deserialising and re-serialising first
    /// changes whitespace and key order and would break every verification, so the caller reads
    /// the body as a string and hands it here untouched.
    /// </summary>
    public WebhookNotification ParseWebhook(
        string webhookSecret, string rawBody, string? signatureHeader, string? eventIdHeader)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret))
            throw new PaymentProviderException("No webhook secret is configured for this account.");

        if (string.IsNullOrWhiteSpace(signatureHeader))
            throw new PaymentSignatureException("The webhook signature header was missing.");

        var expected = HexHmac(rawBody, webhookSecret);
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

            // Razorpay always sends x-razorpay-event-id; the fallback exists so a delivery that
            // somehow lacks it still cannot be processed twice. The event type is folded into the
            // hash so two different event kinds can never collide on one key even if their bodies
            // were byte-identical, and the "body:" prefix keeps a derived key from ever colliding
            // with a real provider event id.
            var eventId = !string.IsNullOrWhiteSpace(eventIdHeader)
                ? eventIdHeader
                : "body:" + Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes($"{eventType}\n{rawBody}"))).ToLowerInvariant();

            return new WebhookNotification
            {
                EventId = eventId,
                EventType = eventType,
                // Razorpay stamps partner deliveries with the sub-merchant's account. Read for
                // cross-checking against the connection the delivery was addressed to — never as
                // the thing that selects which merchant to credit.
                ProviderAccountId = root.TryGetProperty("account_id", out var account)
                    ? account.GetString()
                    : null,
                Outcome = ExtractOutcome(root, eventType)
            };
        }
    }

    // ---- razorpay → domain mapping -------------------------------------

    /// <summary>
    /// Pulls the payment entity out of whichever envelope the event uses.
    ///
    /// order.paid carries both an order and a payment; the payment.* events carry only a payment.
    /// We read the payment entity in every case, so order.paid identifies the *actual* Razorpay
    /// payment by id rather than asserting that "the order is paid" in the abstract. An event
    /// without a payment entity yields no outcome at all: we never invent a successful payment
    /// from an order-level signal alone, because there would be no payment id to deduplicate on.
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

    /// <summary>
    /// Builds one request carrying one merchant's credentials. Both authentication schemes are
    /// applied to the request message, never to the shared client.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        MerchantPaymentContext context, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        Authorise(request, context);

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

    private static void Authorise(HttpRequestMessage request, MerchantPaymentContext context)
    {
        var credentials = context.Credentials;

        switch (credentials.Mode)
        {
            case MerchantConnectionMode.KeyPair:
                if (string.IsNullOrWhiteSpace(credentials.KeySecret))
                    throw new PaymentCredentialException("This account has no usable Razorpay key secret.");

                request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(
                        Encoding.UTF8.GetBytes($"{credentials.PublicKey}:{credentials.KeySecret}")));
                break;

            case MerchantConnectionMode.Oauth:
                if (string.IsNullOrWhiteSpace(credentials.AccessToken))
                    throw new PaymentCredentialException("This account has no usable Razorpay access token.");

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

                // Under OAuth the token belongs to the partner application; this header is what
                // says which sub-merchant's account the call acts on. Without it Razorpay would
                // apply the call to the partner's own account.
                if (!string.IsNullOrWhiteSpace(credentials.ProviderAccountId))
                    request.Headers.Add("X-Razorpay-Account", credentials.ProviderAccountId);
                break;

            default:
                throw new PaymentCredentialException("This account's connection type is not supported.");
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

            // A rejected credential is a different problem from a provider hiccup: it is the
            // merchant's to fix, and the connection should be marked rather than retried.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new PaymentCredentialException(
                    "Razorpay rejected this business's payment credentials. The account needs to be reconnected.");

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
