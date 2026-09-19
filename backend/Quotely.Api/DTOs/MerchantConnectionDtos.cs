using System.ComponentModel.DataAnnotations;
using Quotely.Api.Models;

namespace Quotely.Api.DTOs;

/// <summary>
/// A merchant payment connection as its owner may see it.
///
/// Every secret is absent by construction rather than by being stripped later: there is no key
/// secret, access token or refresh token field on this type at all. The one exception is
/// <see cref="WebhookSecret"/>, which is populated only in the single response that creates it
/// and is never read back from the database afterwards.
///
/// The connection's row id is not here either. The owner has no use for it, and an internal
/// identifier in a response is an internal identifier in a log, a bug report and a browser history.
/// </summary>
public class MerchantConnectionDto
{
    public string Provider { get; set; } = PaymentProviders.Razorpay;

    public MerchantConnectionStatus Status { get; set; }

    public MerchantConnectionMode? Mode { get; set; }

    /// <summary>Whether this account handles test money or real money.</summary>
    public PaymentEnvironment Environment { get; set; }

    /// <summary>Which account is attached — an acc_ identifier or a truncated publishable key.</summary>
    public string? AccountLabel { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>Why the connection needs attention, in words meant for the owner.</summary>
    public string? StatusMessage { get; set; }

    public DateTime? ConnectedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? AccessTokenExpiresAt { get; set; }

    /// <summary>
    /// The single question the invoice page needs answered. True only when the connection is
    /// usable AND its environment matches this deployment's.
    /// </summary>
    public bool CanAcceptPayments { get; set; }

    /// <summary>
    /// Whether "Connect with Razorpay" can be offered. False until Quotely is an approved
    /// Razorpay Technology Partner and the OAuth application is configured — the UI then leads
    /// with API keys instead rather than showing a button that cannot work.
    /// </summary>
    public bool OauthAvailable { get; set; }

    /// <summary>Whether connecting with the merchant's own API keys is offered.</summary>
    public bool KeyPairAvailable { get; set; }

    /// <summary>Where this merchant's Razorpay webhooks should be delivered. Not a secret.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Shown ONCE, in the response that created it, because only the encrypted form is kept.
    /// Null on every subsequent read — which is the honest answer, not an omission.
    /// </summary>
    public string? WebhookSecret { get; set; }
}

/// <summary>Where to send the merchant to authorise us, and the state value to expect back.</summary>
public record MerchantConnectionStartDto(string AuthorizationUrl, string State);

/// <summary>
/// The merchant's own Razorpay API key pair.
///
/// The secret arrives once, over HTTPS, in a request body — never in a URL or a query string,
/// where it would be written to access logs and browser history. It is encrypted before it is
/// stored and is never returned by any endpoint.
/// </summary>
public class ConnectRazorpayKeysRequest
{
    [Required(ErrorMessage = "Enter your Razorpay Key ID.")]
    [StringLength(80, MinimumLength = 8)]
    public string KeyId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your Razorpay Key Secret.")]
    [StringLength(200, MinimumLength = 8)]
    public string KeySecret { get; set; } = string.Empty;

    /// <summary>Optional label, so an owner with more than one account knows which is attached.</summary>
    [StringLength(100)]
    public string? DisplayName { get; set; }
}

/// <summary>What Razorpay sends back to the callback. Both values are untrusted until checked.</summary>
public class CompleteRazorpayOauthRequest
{
    [Required]
    [StringLength(2048)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string State { get; set; } = string.Empty;
}
