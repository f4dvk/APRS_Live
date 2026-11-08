using AprsLive.Core.Aprs;
using Mapsui.Styles;

namespace AprsLive.App.Map;

/// <summary>
/// Maps an APRS symbol (table + code) to 24×24 cells of the hessu/aprs-symbols
/// sprite sheets, embedded in this assembly for Mapsui's "embedded://" image
/// source (a .NET manifest resource, not an Avalonia <c>avares://</c> asset —
/// see <see cref="AprsLive.App.Icons.AprsSymbolIcons"/> for the Avalonia-side
/// equivalent used by the station list and icon picker).
///   sheet 0 = primary table '/', sheet 1 = alternate '\',
///   sheet 2 = the overlay characters (0-9, A-Z) drawn on top of an
///   alternate symbol when the table position holds one of them.
/// The sheets are 16 columns × 6 rows; cell index = char - 0x21 (chars 0x21..0x7E).
/// </summary>
public static class AprsSymbolSprite
{
    private const int Cell = 24;
    private const int Cols = 16;

    private static readonly string[] Sheets =
    {
        "embedded://AprsLive.App.Assets.aprs-symbols-24-0.png",
        "embedded://AprsLive.App.Assets.aprs-symbols-24-1.png",
        "embedded://AprsLive.App.Assets.aprs-symbols-24-2.png",
    };

    // Reuse one Image instance per cell so Mapsui keeps its decoded bitmap
    // cached instead of re-fetching on every redraw.
    private static readonly Dictionary<(int, char), Image> Cache = new();

    /// <summary>The images to stack for this symbol: the symbol, then its overlay character if any.</summary>
    public static IEnumerable<Image> Layers(char table, char code)
    {
        var sprite = AprsSymbol.ResolveSprite(table, code);
        yield return CellImage(sprite.Sheet, sprite.Code);
        if (sprite.Overlay is char overlay)
            yield return CellImage(2, overlay);
    }

    private static Image CellImage(int sheet, char c)
    {
        var key = (sheet, c);
        if (Cache.TryGetValue(key, out var img))
            return img;

        int idx = c - '!';
        img = new Image
        {
            Source = Sheets[sheet],
            BitmapRegion = new BitmapRegion(idx % Cols * Cell, idx / Cols * Cell, Cell, Cell)
        };
        Cache[key] = img;
        return img;
    }
}
