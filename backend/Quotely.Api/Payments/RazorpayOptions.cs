namespace Quotely.Api.Payments;

/// <summary>
/// Razorpay credentials. Bound from configuration only — never hard-coded, never checked in.
/// Supply them through user secrets in development and environment variables in production:
/// Razorpay__KeyId, Razorpay__KeySecret, Razorpay__WebhookSecret.
/// </summary>
public class RazorpayOptions
{
    public const string SectionName = "Razorpay";

    /// <summary>Publishable. Safe to hand to the browser.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Secret. Used to call the API and to verify checkout signatures. Server-only.</summary>
    public string KeySecret { get; set; } = string.Empty;

    /// <summary>Secret. Used to verify webhook signatures. Server-only.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.razorpay.com/v1/";

    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret);
    public bool WebhooksConfigured => !string.IsNullOrWhiteSpace(WebhookSecret);
}
