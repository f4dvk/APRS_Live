using Avalonia.Media;
using AprsLive.Core.Aprs;

namespace AprsLive.App.Icons;

/// <summary>One selectable entry of the station icon picker: the symbol plus its cropped sprite.</summary>
public sealed record SymbolChoice(AprsSymbol.Entry Entry, IImage Icon)
{
    public override string ToString() => Entry.ToString();

    public static SymbolChoice For(AprsSymbol.Entry entry) =>
        new(entry, AprsSymbolIcons.CellImage(entry.Table, entry.Code));
}
