using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Quotely.Api.Models;
using Quotely.Api.Payments;
using Quotely.Api.Security;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// What production refuses to start with.
///
/// These are the checks that stand between a deployment and being quietly wrong about money: a
/// live mode running on test keys, a merchant credential store with no encryption key, a wildcard
/// origin. Every one of them fails silently at runtime days later, which is why they are made to
/// fail loudly at start-up instead.
/// </summary>
public class ProductionStartupTests
{
    private const string GoodJwtKey = "a-signing-key-that-is-at-least-32-characters";
    private const string GoodEncryptionKey = "dGVzdC1vbmx5LWtleS1kby1ub3QtdXNlLWluLXByb2Q=";

    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Quotely.Api";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static IConfiguration Config(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = GoodJwtKey,
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=quotely",
            ["Database:AutoMigrate"] = "false",
            ["PublicLinks:BaseUrl"] = "https://quotely.example"
        };

        foreach (var (key, value) in overrides) values[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static void Validate(
        IConfiguration? configuration = null,
        RazorpayOptions? razorpay = null,
        EncryptionOptions? encryption = null,
        string[]? origins = null,
        string? environment = null)
    {
        ProductionStartupCheck.Validate(
            configuration ?? Config(),
            new Environment { EnvironmentName = environment ?? Environments.Production },
            razorpay ?? new RazorpayOptions { Mode = PaymentEnvironment.Test, KeyId = "rzp_test_x", KeySecret = "s" },
            encryption ?? new EncryptionOptions { Key = GoodEncryptionKey },
            origins ?? new[] { "https://quotely.example" });
    }

    [Fact]
    public void A_correctly_configured_production_deployment_starts()
    {
        var act = () => Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void Development_is_never_checked()
    {
        // A developer who cannot start the application cannot fix it. Placeholders are the point.
        var act = () => Validate(
            configuration: Config(("Jwt:Key", ""), ("ConnectionStrings:DefaultConnection", "")),
            encryption: new EncryptionOptions(),
            origins: new[] { "http://localhost:3000" },
            environment: Environments.Development);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void A_weak_or_missing_signing_key_stops_the_deployment(string key)
    {
        var act = () => Validate(Config(("Jwt:Key", key)));
        act.Should().Throw<InvalidOperationException>().WithMessage("*JWT__KEY*");
    }

    [Fact]
    public void A_missing_connection_string_stops_the_deployment()
    {
        var act = () => Validate(Config(("ConnectionStrings:DefaultConnection", "")));
        act.Should().Throw<InvalidOperationException>().WithMessage("*DefaultConnection*");
    }

    [Fact]
    public void Automatic_migration_on_start_up_stops_the_deployment()
    {
        // An unreviewed schema change running against real customer data whenever a container
        // restarts. A development convenience, never a production one.
        var act = () => Validate(Config(("Database:AutoMigrate", "true")));
        act.Should().Throw<InvalidOperationException>().WithMessage("*AutoMigrate*");
    }

    [Fact]
    public void The_demo_seeder_stops_the_deployment()
    {
        // It creates an account with a known password and writes that password to the log.
        var act = () => Validate(Config(("Seed:Enabled", "true")));
        act.Should().Throw<InvalidOperationException>().WithMessage("*Seed__Enabled*");
    }

    [Fact]
    public void A_missing_encryption_key_stops_the_deployment()
    {
        // Without it no business can connect a payment account at all, and the failure would
        // otherwise appear the first time somebody tried.
        var act = () => Validate(encryption: new EncryptionOptions());
        act.Should().Throw<InvalidOperationException>().WithMessage("*Encryption__Key*");
    }

    [Fact]
    public void An_encryption_key_of_the_wrong_length_stops_the_deployment()
    {
        var act = () => Validate(encryption: new EncryptionOptions { Key = "c2hvcnQ=" });
        act.Should().Throw<InvalidOperationException>().WithMessage("*Encryption__Key*");
    }

    // ---- the money-mode checks -------------------------------------------

    [Fact]
    public void Live_mode_without_credentials_stops_the_deployment()
    {
        // NEVER a silent fall back to test. A live deployment quietly running on test keys would
        // take subscriptions that never arrive.
        var act = () => Validate(razorpay: new RazorpayOptions { Mode = PaymentEnvironment.Live });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Razorpay__KeyId*")
            .And.Message.Should().Contain("Razorpay__WebhookSecret");
    }

    [Fact]
    public void Live_mode_with_test_credentials_stops_the_deployment()
    {
        var act = () => Validate(razorpay: new RazorpayOptions
        {
            Mode = PaymentEnvironment.Live,
            KeyId = "rzp_test_still_a_test_key",
            KeySecret = "s",
            WebhookSecret = "w"
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*not a live key*");
    }

    [Fact]
    public void Live_credentials_in_a_test_deployment_stop_the_deployment()
    {
        // The dangerous direction. Real money collected by a deployment that believes it is
        // testing is worse than the reverse, and is refused rather than warned about.
        var act = () => Validate(razorpay: new RazorpayOptions
        {
            Mode = PaymentEnvironment.Test,
            KeyId = "rzp_live_real_money",
            KeySecret = "s"
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*is a live key*");
    }

    [Fact]
    public void A_fully_configured_live_deployment_starts()
    {
        var act = () => Validate(razorpay: new RazorpayOptions
        {
            Mode = PaymentEnvironment.Live,
            KeyId = "rzp_live_real",
            KeySecret = "s",
            WebhookSecret = "w"
        });

        act.Should().NotThrow();
    }

    // ---- the browser's side ----------------------------------------------

    public static TheoryData<string[]> BadOrigins => new()
    {
        Array.Empty<string>(),
        new[] { "*" },
        new[] { "https://quotely.example", "https://*.quotely.example" },
        new[] { "http://quotely.example" }
    };

    [Theory]
    [MemberData(nameof(BadOrigins))]
    public void A_missing_wildcard_or_insecure_origin_stops_the_deployment(string[] origins)
    {
        var act = () => Validate(origins: origins);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Cors__AllowedOrigins*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://quotely.example")]
    public void A_missing_or_insecure_public_link_base_stops_the_deployment(string baseUrl)
    {
        // Public quotation and invoice links are built from this. Over plain HTTP the token in
        // the URL is readable by anything between the customer and us.
        var act = () => Validate(Config(("PublicLinks:BaseUrl", baseUrl)));
        act.Should().Throw<InvalidOperationException>().WithMessage("*PublicLinks__BaseUrl*");
    }

    // ---- the failure itself ----------------------------------------------

    [Fact]
    public void Every_problem_is_reported_at_once_rather_than_one_per_restart()
    {
        var act = () => Validate(
            configuration: Config(("Jwt:Key", ""), ("ConnectionStrings:DefaultConnection", "")),
            encryption: new EncryptionOptions(),
            origins: new[] { "*" });

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;

        message.Should().Contain("JWT__KEY");
        message.Should().Contain("DefaultConnection");
        message.Should().Contain("Encryption__Key");
        message.Should().Contain("Cors__AllowedOrigins");
    }

    [Fact]
    public void The_failure_names_the_settings_and_never_their_values()
    {
        // A start-up failure is written to a log read by more people than the secret store is.
        var act = () => Validate(razorpay: new RazorpayOptions
        {
            Mode = PaymentEnvironment.Live,
            KeyId = "rzp_test_visible",
            KeySecret = "the_secret_that_must_not_be_logged",
            WebhookSecret = "the_webhook_secret_that_must_not_be_logged"
        });

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;

        message.Should().NotContain("the_secret_that_must_not_be_logged");
        message.Should().NotContain("the_webhook_secret_that_must_not_be_logged");
        message.Should().NotContain(GoodJwtKey);
        message.Should().NotContain(GoodEncryptionKey);
    }
}
