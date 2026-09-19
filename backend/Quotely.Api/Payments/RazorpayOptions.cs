using Quotely.Api.Models;

namespace Quotely.Api.Payments;

/// <summary>
/// QUOTELY'S OWN Razorpay account — the one that collects ₹150/month SaaS subscriptions from the
/// businesses using Quotely.
///
/// Read that sentence again before adding anything here. These credentials must never be used to
/// collect a customer's invoice payment: that money belongs to the business, not to us, and it is
/// collected with the business's own credentials from <see cref="MerchantPaymentConnection"/>.
/// The separation is the point of this milestone.
///
/// Bound from configuration only — Razorpay__KeyId, Razorpay__KeySecret, Razorpay__WebhookSecret.
/// Never hard-coded, never checked in.
/// </summary>
public class RazorpayOptions
{
    public const string SectionName = "Razorpay";

    /// <summary>
    /// Test money or real money, for Quotely's own subscription billing. Explicit configuration
    /// rather than something inferred from the key's prefix: a deployment must state which kind of
    /// money it handles, and be refused at startup when the credentials do not match that claim.
    /// </summary>
    public PaymentEnvironment Mode { get; set; } = PaymentEnvironment.Test;

    /// <summary>Publishable. Safe to hand to the browser on the billing page.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Secret. Server-only.</summary>
    public string KeySecret { get; set; } = string.Empty;

    /// <summary>Secret. Verifies subscription webhook signatures. Server-only.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.razorpay.com/v1/";

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>The Technology Partner OAuth application, once Razorpay has approved one.</summary>
    public RazorpayOauthOptions Oauth { get; set; } = new();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret);
    public bool WebhooksConfigured => !string.IsNullOrWhiteSpace(WebhookSecret);

    /// <summary>
    /// Whether the key's own prefix agrees with the mode this deployment declares. Razorpay
    /// prefixes test keys "rzp_test_" and live keys "rzp_live_", so a mismatch means either the
    /// mode or the credentials are wrong — and in production that is a reason to refuse to start,
    /// not a warning to log and carry on past.
    /// </summary>
    public bool ModeMatchesCredentials()
    {
        if (string.IsNullOrWhiteSpace(KeyId)) return true;

        return Mode switch
        {
            PaymentEnvironment.Live => KeyId.StartsWith("rzp_live_", StringComparison.Ordinal),
            PaymentEnvironment.Test => !KeyId.StartsWith("rzp_live_", StringComparison.Ordinal),
            _ => false
        };
    }
}

/// <summary>
/// Razorpay Technology Partner OAuth credentials, used to let a business connect their own
/// Razorpay account without ever handing us their key secret.
///
/// These do not exist until Razorpay approves Quotely as a Technology Partner — see
/// docs/razorpay-partner.md for what that involves. Until then the section is empty, OAuth
/// reports itself unavailable, and businesses connect with their own API keys instead. Nothing
/// here is faked or stubbed: an unconfigured OAuth application is simply switched off.
/// </summary>
public class RazorpayOauthOptions
{
    /// <summary>From the Razorpay Partner Dashboard, under Partners &gt; Applications.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Secret. Server-only.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Where Razorpay returns the merchant after they authorise us. Must exactly match a URI
    /// whitelisted on the Razorpay application, and must be HTTPS for a production client.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Razorpay's OAuth endpoints. Configurable only so a test can point them elsewhere; the
    /// defaults are the documented production values and should not be changed in a deployment.
    /// </summary>
    public string AuthorizeUrl { get; set; } = "https://auth.razorpay.com/authorize";
    public string TokenUrl { get; set; } = "https://auth.razorpay.com/token";
    public string RevokeUrl { get; set; } = "https://auth.razorpay.com/revoke";

    /// <summary>
    /// The permissions we ask a merchant for. read_write covers creating orders and reading
    /// payments, which is the whole of what Quotely does with their account — we deliberately do
    /// not request any rx_* scope, because Quotely has no business touching a merchant's banking.
    /// </summary>
    public string Scope { get; set; } = "read_write";

    /// <summary>
    /// How long before expiry an access token is renewed. Razorpay issues them for roughly 91
    /// days; renewing a week early means a refresh failure is noticed with six days to spare
    /// rather than at the moment a customer tries to pay.
    /// </summary>
    public int RefreshWindowDays { get; set; } = 7;

    /// <summary>
    /// How long a merchant has to complete the Razorpay authorisation before the pending
    /// connection is abandoned. Short, because the whole flow is a single redirect and back.
    /// </summary>
    public int AuthorizationTimeoutMinutes { get; set; } = 15;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(RedirectUri);
}
