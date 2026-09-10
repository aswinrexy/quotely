using QuestPDF.Drawing;

namespace Quotely.Api.Pdf;

/// <summary>
/// Registers the PDF typeface once at startup. Any .ttf dropped into Pdf/Fonts is registered;
/// "Noto Sans" is preferred because it carries the currency glyphs (₹, €, …) that the
/// QuestPDF built-in font is missing. Falls back to Lato when no font files ship with the build.
/// </summary>
public static class PdfFonts
{
    private const string Preferred = "Noto Sans";
    private const string Fallback = "Lato";

    public static string Family { get; private set; } = Fallback;

    public static void Register(string contentRootPath, ILogger? logger = null)
    {
        var directory = Path.Combine(contentRootPath, "Pdf", "Fonts");
        if (!Directory.Exists(directory)) return;

        var registered = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.ttf"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                FontManager.RegisterFont(stream);
                registered++;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not register PDF font {Font}", file);
            }
        }

        if (registered > 0) Family = Preferred;
    }
}
