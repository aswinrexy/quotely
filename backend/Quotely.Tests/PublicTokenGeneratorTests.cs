using FluentAssertions;
using Quotely.Api.Services;
using Xunit;

namespace Quotely.Tests;

public class PublicTokenGeneratorTests
{
    [Fact]
    public void Tokens_are_url_safe_and_long_enough_to_resist_guessing()
    {
        var token = PublicTokenGenerator.CreateToken();

        token.Length.Should().Be(43, "32 random bytes in unpadded base64url");
        token.Should().MatchRegex("^[A-Za-z0-9_-]+$", "the token travels in a URL path");
    }

    [Fact]
    public void Every_token_is_unique()
    {
        var tokens = Enumerable.Range(0, 500).Select(_ => PublicTokenGenerator.CreateToken()).ToList();

        tokens.Distinct().Should().HaveCount(tokens.Count);
    }

    [Fact]
    public void Hashing_is_deterministic_and_hides_the_token()
    {
        var token = PublicTokenGenerator.CreateToken();

        var hash = PublicTokenGenerator.Hash(token);

        hash.Should().Be(PublicTokenGenerator.Hash(token));
        hash.Should().HaveLength(PublicTokenGenerator.HashLength);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
        hash.Should().NotContain(token);
    }

    [Fact]
    public void Different_tokens_hash_differently()
    {
        PublicTokenGenerator.Hash(PublicTokenGenerator.CreateToken())
            .Should().NotBe(PublicTokenGenerator.Hash(PublicTokenGenerator.CreateToken()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("' OR 1=1--")]
    [InlineData("../../etc/passwd")]
    public void Obviously_invalid_tokens_are_rejected_before_any_lookup(string? token)
    {
        PublicTokenGenerator.LooksValid(token).Should().BeFalse();
    }

    [Fact]
    public void A_generated_token_passes_validation()
    {
        PublicTokenGenerator.LooksValid(PublicTokenGenerator.CreateToken()).Should().BeTrue();
    }
}
