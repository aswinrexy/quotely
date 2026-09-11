using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quotely.Api.Models;
using Quotely.Api.Payments;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// Unit tests against the real Razorpay adapter. No network is involved: money conversion,
/// both signature schemes and the webhook parser are all pure functions of their inputs, so
/// these exercise the production code that guards the money.
/// </summary>
public class RazorpayProviderTests
{
    private const string KeyId = "rzp_test_unit";
    private const string KeySecret = "unit_key_secret";
    private const string WebhookSecret = "unit_webhook_secret";

    private static RazorpayPaymentProvider NewProvider() =>
        new(new HttpClient(),
            Options.Create(new RazorpayOptions
            {
                KeyId = KeyId,
                KeySecret = KeySecret,
                WebhookSecret = WebhookSecret
            }),
            NullLogger<RazorpayPaymentProvider>.Instance);

    private static string Hmac(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    // ---- money ----------------------------------------------------------

    [Theory]
    [InlineData("1250.50", 125050)]
    [InlineData("1.00", 100)]
    [InlineData("0.01", 1)]
    [InlineData("10000", 1000000)]
    [InlineData("0.005", 1)]      // half a paise rounds away from zero, as the totals do
    [InlineData("12980.00", 1298000)]
    public void Rupees_convert_to_paise_without_floating_point(string amount, long expected)
    {
        NewProvider().ToMinorUnits(decimal.Parse(amount)).Should().Be(expected);
    }

    [Fact]
    public void A_negative_amount_is_refused()
    {
        var act = () => NewProvider().ToMinorUnits(-1m);
        act.Should().Throw<PaymentProviderException>();
    }

    [Fact]
    public void The_conversion_is_exact_where_a_double_would_drift()
    {
        // 0.1 + 0.2 in binary floating point is 0.30000000000000004, which scales to 30.000000000000004.
        var provider = NewProvider();
        provider.ToMinorUnits(0.1m + 0.2m).Should().Be(30);
        provider.ToMinorUnits(8.29m).Should().Be(829);
    }

    // ---- checkout signature ---------------------------------------------

    [Fact]
    public void A_checkout_signature_over_order_and_payment_is_accepted()
    {
        var signature = Hmac("order_abc|pay_xyz", KeySecret);

        var act = () => NewProvider().VerifyCheckoutSignature(new CheckoutResult("order_abc", "pay_xyz", signature));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("deadbeef")]
    [InlineData("")]
    public void A_wrong_checkout_signature_is_rejected(string signature)
    {
        var act = () => NewProvider().VerifyCheckoutSignature(new CheckoutResult("order_abc", "pay_xyz", signature));

        act.Should().Throw<PaymentSignatureException>();
    }

    [Fact]
    public void A_signature_for_a_different_order_is_rejected()
    {
        // A valid signature from someone else's order must not open ours.
        var signature = Hmac("order_other|pay_xyz", KeySecret);

        var act = () => NewProvider().VerifyCheckoutSignature(new CheckoutResult("order_abc", "pay_xyz", signature));

        act.Should().Throw<PaymentSignatureException>();
    }

    [Fact]
    public void A_signature_made_with_the_wrong_secret_is_rejected()
    {
        var signature = Hmac("order_abc|pay_xyz", "not_our_secret");

        var act = () => NewProvider().VerifyCheckoutSignature(new CheckoutResult("order_abc", "pay_xyz", signature));

        act.Should().Throw<PaymentSignatureException>();
    }

    // ---- webhook signature ----------------------------------------------

    private static string Body(string eventType = "payment.captured", string status = "captured", long amount = 125050) =>
        $"{{\"event\":\"{eventType}\",\"payload\":{{\"payment\":{{\"entity\":{{\"id\":\"pay_1\",\"order_id\":\"order_1\"," +
        $"\"status\":\"{status}\",\"amount\":{amount},\"currency\":\"INR\",\"method\":\"upi\"}}}}}}}}";

    [Fact]
    public void A_webhook_signed_over_the_raw_body_is_accepted()
    {
        var body = Body();

        var notification = NewProvider().ParseWebhook(body, Hmac(body, WebhookSecret), "evt_1");

        notification.EventId.Should().Be("evt_1");
        notification.EventType.Should().Be("payment.captured");
        notification.Outcome!.Status.Should().Be(PaymentStatus.Captured);
        notification.Outcome.ProviderPaymentId.Should().Be("pay_1");
        notification.Outcome.AmountInMinorUnits.Should().Be(125050);
        notification.Outcome.Method.Should().Be("upi");
    }

    [Fact]
    public void A_signature_computed_over_reserialised_json_is_rejected()
    {
        // This is the mistake the raw-body rule exists to prevent. Razorpay sends indented JSON;
        // parsing and re-emitting it changes the bytes, so a signature computed over the
        // round-tripped text no longer matches what was actually sent.
        var body = System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonDocument.Parse(Body()).RootElement,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var reserialised = System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonDocument.Parse(body).RootElement);

        reserialised.Should().NotBe(body);

        var act = () => NewProvider().ParseWebhook(body, Hmac(reserialised, WebhookSecret), "evt_1");

        act.Should().Throw<PaymentSignatureException>();
    }

    [Fact]
    public void A_missing_or_wrong_webhook_signature_is_rejected()
    {
        var provider = NewProvider();
        var body = Body();

        var missing = () => provider.ParseWebhook(body, null, "evt_1");
        missing.Should().Throw<PaymentSignatureException>();

        var wrong = () => provider.ParseWebhook(body, Hmac(body, "other_secret"), "evt_1");
        wrong.Should().Throw<PaymentSignatureException>();
    }

    [Fact]
    public void A_webhook_without_an_event_id_header_falls_back_to_a_body_hash()
    {
        var body = Body();
        var provider = NewProvider();

        var first = provider.ParseWebhook(body, Hmac(body, WebhookSecret), null);
        var second = provider.ParseWebhook(body, Hmac(body, WebhookSecret), null);

        // Deterministic, so an identical redelivery still deduplicates.
        first.EventId.Should().Be(second.EventId).And.HaveLength(64);
    }

    [Fact]
    public void Razorpay_statuses_map_onto_our_own_vocabulary()
    {
        var provider = NewProvider();

        Parse(provider, "authorized").Should().Be(PaymentStatus.Pending, "authorised money is not captured money");
        Parse(provider, "captured").Should().Be(PaymentStatus.Captured);
        Parse(provider, "failed").Should().Be(PaymentStatus.Failed);

        static PaymentStatus Parse(RazorpayPaymentProvider provider, string status)
        {
            var body = Body(status: status);
            return provider.ParseWebhook(body, Hmac(body, WebhookSecret), "evt")!.Outcome!.Status;
        }
    }

    [Fact]
    public void An_unhandled_event_type_parses_but_carries_no_outcome()
    {
        var body = "{\"event\":\"refund.created\",\"payload\":{}}";

        var notification = NewProvider().ParseWebhook(body, Hmac(body, WebhookSecret), "evt_r");

        notification.Outcome.Should().BeNull();
    }

    [Fact]
    public void A_captured_payment_outranks_an_authorised_one()
    {
        // Webhook ordering is not guaranteed, so rank decides what may overwrite what.
        PaymentStatus.Captured.SupersedesOrEquals(PaymentStatus.Pending).Should().BeTrue();
        PaymentStatus.Pending.SupersedesOrEquals(PaymentStatus.Captured).Should().BeFalse();
        PaymentStatus.Failed.SupersedesOrEquals(PaymentStatus.Captured).Should().BeFalse();
        PaymentStatus.Captured.SupersedesOrEquals(PaymentStatus.Captured).Should().BeTrue();
    }

    [Fact]
    public void An_unconfigured_provider_refuses_rather_than_calling_out()
    {
        var provider = new RazorpayPaymentProvider(
            new HttpClient(),
            Options.Create(new RazorpayOptions()),
            NullLogger<RazorpayPaymentProvider>.Instance);

        provider.IsConfigured.Should().BeFalse();

        var act = async () => await provider.CreateOrderAsync(
            new CreateOrderRequest(100m, "INR", "INV-000001", Guid.NewGuid()));

        act.Should().ThrowAsync<PaymentProviderException>();
    }
}
