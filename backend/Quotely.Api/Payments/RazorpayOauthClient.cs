using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Quotely.Api.Models;

namespace Quotely.Api.Payments;

/// <summary>
/// Razorpay's OAuth endpoints, exactly as Razorpay documents them for Technology Partners. No
/// endpoint, parameter or scope in this file is invented: see docs/razorpay-partner.md for the
/// documentation each one comes from.
///
/// This talks to auth.razorpay.com, which is a different host from the API the rest of the
/// payment code uses, and it authenticates as the PARTNER APPLICATION rather than as any
/// merchant. It is the one place Quotely's partner client secret is used.
/// </summary>
public interface IRazorpayOauthClient
{
    /// <summary>True when a partner application has been configured. False until approval.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Where to send the merchant to authorise us. <paramref name="state"/> is echoed back and
    /// must be checked on return — it is what stops someone else's authorisation code being
    /// planted in this merchant's callback.
    /// </summary>
    string BuildAuthorizationUrl(string state);

    /// <summary>Exchanges the one-time code from the callback for tokens.</summary>
    Task<RazorpayOauthTokens> ExchangeCodeAsync(string code, PaymentEnvironment mode, CancellationToken ct = default);

    /// <summary>Renews an access token before it expires. Razorpay rotates the refresh token too.</summary>
    Task<RazorpayOauthTokens> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// Tells Razorpay we are done with a token. Best-effort: a merchant disconnecting must
    /// succeed locally even if Razorpay cannot be reached, or a provider outage would trap them
    /// in a connection they have asked to end.
    /// </summary>
    Task<bool> RevokeAsync(string token, string tokenTypeHint, CancellationToken ct = default);
}

/// <summary>
/// What Razorpay returns from the token endpoint. <c>PublicToken</c> — not <c>AccessToken</c> —
/// is what Checkout is initialised with in the browser; handing the access token to a browser
/// would give it the merchant's whole API.
/// </summary>
public sealed record RazorpayOauthTokens(
    string AccessToken,
    string PublicToken,
    string? RefreshToken,
    string? RazorpayAccountId,
    DateTime ExpiresAt);

public class RazorpayOauthClient : IRazorpayOauthClient
{
    private readonly HttpClient _http;
    private readonly RazorpayOauthOptions _options;
    private readonly ILogger<RazorpayOauthClient> _logger;

    public RazorpayOauthClient(
        HttpClient http, IOptions<RazorpayOptions> options, ILogger<RazorpayOauthClient> logger)
    {
        _http = http;
        _options = options.Value.Oauth;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
    }

    public bool IsConfigured => _options.IsConfigured;

    public string BuildAuthorizationUrl(string state)
    {
        EnsureConfigured();

        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = _options.RedirectUri,
            ["scope"] = _options.Scope,
            ["state"] = state
        };

        var encoded = string.Join('&', query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"{_options.AuthorizeUrl}?{encoded}";
    }

    public Task<RazorpayOauthTokens> ExchangeCodeAsync(
        string code, PaymentEnvironment mode, CancellationToken ct = default)
    {
        EnsureConfigured();

        return PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = _options.RedirectUri,
            ["code"] = code,
            // Razorpay defaults this to live. Saying it explicitly is what keeps a test
            // deployment from minting tokens against a merchant's real account.
            ["mode"] = mode == PaymentEnvironment.Live ? "live" : "test"
        }, "code exchange", ct);
    }

    public Task<RazorpayOauthTokens> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        EnsureConfigured();

        return PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        }, "token refresh", ct);
    }

    public async Task<bool> RevokeAsync(string token, string tokenTypeHint, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        var payload = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["token_type_hint"] = tokenTypeHint,
            ["token"] = token
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.RevokeUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return true;

            _logger.LogWarning("Razorpay token revocation returned {Status}", (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Deliberately swallowed. The merchant's local disconnection is what matters; a
            // token we could not revoke remotely is recorded as such and retried by the caller's
            // own logic rather than blocking them here.
            _logger.LogWarning(ex, "Razorpay token revocation could not be delivered");
            return false;
        }
    }

    private async Task<RazorpayOauthTokens> PostTokenAsync(
        Dictionary<string, string> payload, string operation, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Razorpay {Operation} could not be delivered", operation);
            throw new PaymentProviderException("Razorpay could not be reached. Please try again.");
        }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // The body can echo the client_id and Razorpay's own error vocabulary. Logged at
                // a length that is useful for diagnosis, never returned to the browser.
                _logger.LogWarning(
                    "Razorpay {Operation} returned {Status}: {Body}",
                    operation, (int)response.StatusCode, content.Length <= 500 ? content : content[..500]);

                throw new PaymentCredentialException(
                    "Razorpay did not complete the connection. Please try connecting again.");
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;

                var accessToken = Read(root, "access_token")
                    ?? throw new PaymentProviderException("Razorpay returned no access token.");

                // Razorpay states this in seconds. Falling back to 90 days rather than to "never
                // expires": an unknown expiry that we treat as infinite is a connection that
                // silently stops working, whereas one we renew early costs a single API call.
                var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.ValueKind == JsonValueKind.Number
                    ? expires.GetInt64()
                    : TimeSpan.FromDays(90).Ticks / TimeSpan.TicksPerSecond;

                return new RazorpayOauthTokens(
                    accessToken,
                    // The browser-safe key. Falls back to the access token only in the sense that
                    // it must not: an absent public token is a malformed response.
                    Read(root, "public_token")
                        ?? throw new PaymentProviderException("Razorpay returned no public token."),
                    Read(root, "refresh_token"),
                    Read(root, "razorpay_account_id"),
                    DateTime.UtcNow.AddSeconds(expiresIn));
            }
            catch (JsonException)
            {
                throw new PaymentProviderException("Razorpay returned an unexpected response.");
            }
        }
    }

    private static string? Read(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new PaymentProviderException(
                "Connecting with Razorpay is not available on this deployment yet.");
    }
}
