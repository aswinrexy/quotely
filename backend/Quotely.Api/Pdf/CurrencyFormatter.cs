using System.Globalization;

namespace Quotely.Api.Pdf;

public static class CurrencyFormatter
{
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["INR"] = "₹",
        ["USD"] = "$",
        ["EUR"] = "€",
        ["GBP"] = "£",
        ["AED"] = "AED ",
        ["SAR"] = "SAR ",
        ["AUD"] = "A$",
        ["CAD"] = "C$",
        ["SGD"] = "S$"
    };

    public static string Symbol(string? currency) =>
        currency is not null && Symbols.TryGetValue(currency, out var symbol) ? symbol : $"{currency} ";

    public static string Format(decimal amount, string? currency)
    {
        var text = amount.ToString("#,##0.00", CultureInfo.InvariantCulture);
        return $"{Symbol(currency)}{text}";
    }

    public static string Quantity(decimal value) =>
        value == Math.Truncate(value)
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Percent(decimal rate) =>
        $"{rate.ToString("0.##", CultureInfo.InvariantCulture)}%";
}
