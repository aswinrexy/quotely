using FluentAssertions;
using Quotely.Api.Services;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// The "amount chargeable in words" line on every trade document.
///
/// Worth testing properly rather than eyeballing once: this line exists so a bank can read the
/// figure when the digits are smudged or altered, which makes it the one number on the page with
/// no second opinion available.
/// </summary>
public class AmountInWordsTests
{
    /// <summary>
    /// The reference proforma's own total. It prints "ONE LAC FIFTY THOUSAND FOUR HUNDERED EIGHTY
    /// ONLY" — Quotely spells "lakh" and "hundred" correctly rather than copying the typos, but
    /// the grouping, the trailing "only" and the leading currency code all match.
    /// </summary>
    [Fact]
    public void The_reference_documents_total_reads_the_way_the_document_does()
    {
        AmountInWords.Format(150480m, "INR")
            .Should().Be("INR One Lakh Fifty Thousand Four Hundred Eighty Only");
    }

    [Fact]
    public void The_same_number_groups_differently_outside_India()
    {
        // Not a formatting preference. An American buyer reading "one lakh" and an Indian bank
        // reading "one hundred fifty thousand" both see a document written by someone else.
        AmountInWords.Format(150480m, "USD")
            .Should().Be("USD One Hundred Fifty Thousand Four Hundred Eighty Only");
    }

    [Theory]
    [InlineData(0, "INR Zero Only")]
    [InlineData(1, "INR One Only")]
    [InlineData(19, "INR Nineteen Only")]
    [InlineData(20, "INR Twenty Only")]
    [InlineData(21, "INR Twenty-One Only")]
    [InlineData(100, "INR One Hundred Only")]
    [InlineData(101, "INR One Hundred One Only")]
    [InlineData(999, "INR Nine Hundred Ninety-Nine Only")]
    [InlineData(1000, "INR One Thousand Only")]
    [InlineData(100000, "INR One Lakh Only")]
    [InlineData(10000000, "INR One Crore Only")]
    [InlineData(77880, "INR Seventy-Seven Thousand Eight Hundred Eighty Only")]
    public void Indian_grouping_is_correct_across_the_boundaries(int amount, string expected)
    {
        AmountInWords.Format(amount, "INR").Should().Be(expected);
    }

    [Fact]
    public void A_crore_and_a_lakh_combine_rather_than_swallowing_each_other()
    {
        // 1,23,45,678 — the case where a naive three-digit grouping produces "twelve million".
        AmountInWords.Format(12345678m, "INR")
            .Should().Be("INR One Crore Twenty-Three Lakh Forty-Five Thousand Six Hundred Seventy-Eight Only");
    }

    [Fact]
    public void Paise_are_named_and_kept()
    {
        AmountInWords.Format(45.50m, "INR").Should().Be("INR Forty-Five And Fifty Paise Only");
    }

    [Fact]
    public void Each_currency_names_its_own_fractional_unit()
    {
        AmountInWords.Format(12.25m, "AED").Should().Contain("Fils");
        AmountInWords.Format(12.25m, "USD").Should().Contain("Cents");
        AmountInWords.Format(12.25m, "GBP").Should().Contain("Pence");
    }

    [Fact]
    public void The_words_never_disagree_with_the_rounded_figure()
    {
        // 1.999 prints as 2.00, so it must not read as "one and ninety-nine". The two appear side
        // by side on the page and a reader who spots the difference stops trusting both.
        AmountInWords.Format(1.999m, "INR").Should().Be("INR Two Only");
        AmountInWords.Format(0.999m, "INR").Should().Be("INR One Only");
    }

    [Fact]
    public void An_unknown_currency_still_produces_a_readable_line()
    {
        // Rather than throwing on a code we have no unit names for: the document still has to
        // print, and the code carries the meaning.
        AmountInWords.Format(2500m, "XYZ").Should().Be("XYZ Two Thousand Five Hundred Only");
    }

    [Fact]
    public void A_negative_amount_says_so_instead_of_quietly_losing_its_sign()
    {
        AmountInWords.Format(-500m, "INR").Should().StartWith("INR Minus");
    }

    [Fact]
    public void Describe_can_name_the_major_unit_when_it_stands_alone()
    {
        AmountInWords.Describe(150480m, "INR")
            .Should().Be("One Lakh Fifty Thousand Four Hundred Eighty Rupees Only");
        AmountInWords.Describe(1m, "INR").Should().Be("One Rupee Only");
    }

    [Fact]
    public void Very_large_rupee_amounts_fall_back_rather_than_inventing_a_word()
    {
        // Above ninety-nine crore the Indian convention stops agreeing with itself — arab, kharab
        // and "lakh crore" are all in use. Printing a confident guess on a financial document is
        // worse than switching to the system that has one answer.
        var words = AmountInWords.Format(1_000_000_000_000m, "INR");
        words.Should().Contain("Trillion");
        words.Should().NotContain("Arab");
    }
}
