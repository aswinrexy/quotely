using System.Globalization;
using System.Text;

namespace Quotely.Api.Services;

/// <summary>
/// Spells a money amount out in words, for the "Amount chargeable (in words)" line that every
/// trade document carries.
///
/// This line is not decoration. It is the figure a bank reads when the digits are smudged, altered
/// or ambiguous, which is precisely why the convention exists — so it has to be right, and it has
/// to be tested rather than eyeballed once.
///
/// TWO NUMBERING SYSTEMS, chosen by currency:
///
/// INR groups as thousand / lakh / crore — 150,480 is "one lakh fifty thousand four hundred
/// eighty". Every other currency groups as thousand / million / billion, where the same number is
/// "one hundred fifty thousand four hundred eighty". Printing the Indian form on a USD invoice to
/// an American buyer, or the international form on a rupee invoice to an Indian bank, both read as
/// a mistake to the person who matters.
///
/// The reference document spells it "ONE LAC FIFTY THOUSAND FOUR HUNDERED EIGHTY ONLY" — "LAC"
/// and a misspelt "HUNDERED". Quotely spells "lakh" and "hundred" correctly rather than
/// reproducing a typo, and the surrounding shape — the trailing "ONLY", the currency name in
/// front — follows the convention the document is showing.
/// </summary>
public static class AmountInWords
{
    private static readonly string[] Ones =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen"
    };

    private static readonly string[] Tens =
    {
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"
    };

    /// <summary>Currencies whose fractional unit is not called "paise"/"cents" generically.</summary>
    private static readonly Dictionary<string, (string Major, string MajorPlural, string Minor, string MinorPlural)> Units =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["INR"] = ("rupee", "rupees", "paisa", "paise"),
            ["USD"] = ("dollar", "dollars", "cent", "cents"),
            ["AED"] = ("dirham", "dirhams", "fils", "fils"),
            ["EUR"] = ("euro", "euros", "cent", "cents"),
            ["GBP"] = ("pound", "pounds", "penny", "pence"),
            ["SAR"] = ("riyal", "riyals", "halala", "halalas"),
            ["SGD"] = ("dollar", "dollars", "cent", "cents"),
            ["AUD"] = ("dollar", "dollars", "cent", "cents"),
        };

    /// <summary>
    /// The full line as it prints, e.g.
    /// "INR One Lakh Fifty Thousand Four Hundred Eighty Only".
    ///
    /// The currency code leads rather than the spelled-out name because that is what trade
    /// documents do, and because a code is unambiguous where "dollars" is not.
    /// </summary>
    public static string Format(decimal amount, string currency)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? "INR" : currency.Trim().ToUpperInvariant();
        // The code carries the currency, so the major unit is NOT repeated: "INR One Lakh ...
        // Only", not "INR One Lakh ... Rupees Only". That is the form the reference uses, and
        // saying it twice reads like a mistake. The minor unit is kept, because "and fifty" on its
        // own does not say what fifty of.
        return $"{code} {Describe(amount, code, includeMajorUnit: false)}";
    }

    /// <summary>
    /// The words alone, already title-cased and ending "Only".
    /// </summary>
    /// <param name="includeMajorUnit">
    /// Names the major unit inline ("Fifty Rupees Only"). Off when a currency code is printed
    /// alongside, on when these words have to stand by themselves.
    /// </param>
    public static string Describe(decimal amount, string currency, bool includeMajorUnit = true)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? "INR" : currency.Trim().ToUpperInvariant();
        var indian = code == "INR";

        // Negative amounts are not a normal invoice, but a credit note is a real thing and
        // silently printing the absolute value would be a lie.
        var negative = amount < 0;
        amount = Math.Abs(amount);

        // Round to two places FIRST, so 0.999 becomes one whole unit rather than "zero and
        // ninety-nine" — the printed figure and the printed words must not disagree.
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        var whole = (long)decimal.Truncate(amount);
        var fraction = (int)Math.Round((amount - whole) * 100m, 0, MidpointRounding.AwayFromZero);

        // Rounding the fraction can carry: 1.999 -> whole 1, fraction 100.
        if (fraction == 100)
        {
            whole += 1;
            fraction = 0;
        }

        var units = Units.TryGetValue(code, out var u)
            ? u
            : (Major: string.Empty, MajorPlural: string.Empty, Minor: string.Empty, MinorPlural: string.Empty);
        var builder = new StringBuilder();

        if (negative) builder.Append("minus ");

        builder.Append(indian ? IndianWords(whole) : InternationalWords(whole));

        if (includeMajorUnit && !string.IsNullOrEmpty(units.Major))
            builder.Append(' ').Append(whole == 1 ? units.Major : units.MajorPlural);

        if (fraction > 0)
        {
            builder.Append(" and ").Append(InternationalWords(fraction));
            if (!string.IsNullOrEmpty(units.Minor))
                builder.Append(' ').Append(fraction == 1 ? units.Minor : units.MinorPlural);
        }

        builder.Append(" only");
        return TitleCase(builder.ToString());
    }

    /// <summary>
    /// Indian grouping: crore, lakh, then thousand / hundred / tens.
    ///
    /// Note the group sizes are NOT uniform — the lowest group is three digits and every group
    /// above it is two, which is exactly why the international algorithm cannot be reused with a
    /// different word list.
    /// </summary>
    private static string IndianWords(long value)
    {
        if (value == 0) return Ones[0];

        var parts = new List<string>();

        // Above ninety-nine crore the convention stops agreeing with itself — "lakh crore",
        // "arab" and "kharab" are all in use. Rather than pick one and print it confidently on a
        // financial document, hand anything that large to the international system, which does
        // have one unambiguous answer.
        if (value >= 1_00_00_00_00_000L) return InternationalWords(value);

        var crore = value / 1_00_00_000L;
        value %= 1_00_00_000L;
        var lakh = value / 1_00_000L;
        value %= 1_00_000L;
        var thousand = value / 1_000L;
        value %= 1_000L;

        if (crore > 0) parts.Add($"{IndianWords(crore)} crore");
        if (lakh > 0) parts.Add($"{BelowHundred(lakh)} lakh");
        if (thousand > 0) parts.Add($"{BelowHundred(thousand)} thousand");
        if (value > 0) parts.Add(BelowThousand((int)value));

        return string.Join(" ", parts);
    }

    /// <summary>Western grouping: billion, million, thousand, then hundreds.</summary>
    private static string InternationalWords(long value)
    {
        if (value == 0) return Ones[0];

        var parts = new List<string>();
        var scales = new (long Size, string Name)[]
        {
            (1_000_000_000_000L, "trillion"),
            (1_000_000_000L, "billion"),
            (1_000_000L, "million"),
            (1_000L, "thousand"),
        };

        foreach (var (size, name) in scales)
        {
            if (value < size) continue;
            var count = value / size;
            value %= size;
            parts.Add($"{InternationalWords(count)} {name}");
        }

        if (value > 0) parts.Add(BelowThousand((int)value));
        return string.Join(" ", parts);
    }

    private static string BelowThousand(int value)
    {
        if (value >= 100)
        {
            var hundreds = $"{Ones[value / 100]} hundred";
            var rest = value % 100;
            return rest == 0 ? hundreds : $"{hundreds} {BelowHundred(rest)}";
        }

        return BelowHundred(value);
    }

    private static string BelowHundred(long value)
    {
        if (value < 20) return Ones[value];
        var tens = Tens[value / 10];
        var ones = value % 10;
        return ones == 0 ? tens : $"{tens}-{Ones[ones]}";
    }

    /// <summary>
    /// Title case for the printed line. Hyphenated compounds get both halves capitalised
    /// ("Forty-Five"), which is what a typed invoice does.
    /// </summary>
    private static string TitleCase(string value)
    {
        var info = CultureInfo.InvariantCulture.TextInfo;
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => string.Join("-", word.Split('-').Select(info.ToTitleCase)));
        return string.Join(" ", words);
    }
}
