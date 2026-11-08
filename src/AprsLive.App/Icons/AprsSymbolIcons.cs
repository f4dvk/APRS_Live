using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AprsLive.Core.Aprs;

namespace AprsLive.App.Icons;

/// <summary>
/// Crops 24×24 cells out of the hessu/aprs-symbols sprite sheets (embedded
/// as Avalonia resources) for a given APRS symbol (table + code). Sheet 0 is
/// the primary table (<c>/</c>), sheet 1 the alternate table (<c>\</c>) and
/// sheet 2 the overlay characters (0-9, A-Z), stacked on the alternate symbol
/// when the table position holds one. Sheets are 16 columns × 6 rows,
/// cell index = char - '!' (chars '!'..'~').
/// </summary>
public static class AprsSymbolIcons
{
    private const int Cell = 24;
    private const int Cols = 16;

    // Authority is the assembly name (AssemblyName in the csproj), not the
    // project/namespace name — this app builds as "APRS_Live.dll".
    private static readonly Lazy<Bitmap>[] Sheets =
    [
        new(() => Load("aprs-symbols-24-0.png")),
        new(() => Load("aprs-symbols-24-1.png")),
        new(() => Load("aprs-symbols-24-2.png")),
    ];

    private static readonly Dictionary<(char Table, char Code), IImage> Cache = new();

    private static Bitmap Load(string name) =>
        new(AssetLoader.Open(new Uri($"avares://APRS_Live/Assets/{name}")));

    public static IImage CellImage(char table, char code)
    {
        var key = (table, code);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var sprite = AprsSymbol.ResolveSprite(table, code);
        IImage image = Crop(sprite.Sheet, sprite.Code);
        if (sprite.Overlay is char overlay)
        {
            var full = new Rect(0, 0, Cell, Cell);
            image = new DrawingImage(new DrawingGroup
            {
                Children =
                {
                    new ImageDrawing { ImageSource = image, Rect = full },
                    new ImageDrawing { ImageSource = Crop(2, overlay), Rect = full },
                }
            });
        }

        Cache[key] = image;
        return image;
    }

    private static CroppedBitmap Crop(int sheet, char c)
    {
        int idx = c - '!';
        return new CroppedBitmap(Sheets[sheet].Value,
            new PixelRect(idx % Cols * Cell, idx / Cols * Cell, Cell, Cell));
    }
}
