using AprsLive.Core.Aprs;

namespace AprsLive.Core.Tests;

public class AprsSymbolTests
{
    [Theory]
    [InlineData('/', '>', 0, '>', null)]          // primary car
    [InlineData('\\', '#', 1, '#', null)]         // alternate digi, no overlay
    [InlineData('R', '#', 1, '#', 'R')]           // overlaid digi (e.g. "R#")
    [InlineData('1', '&', 1, '&', '1')]           // overlaid gateway
    [InlineData('c', '#', 1, '#', '2')]           // compressed overlay: a-j stand for 0-9
    [InlineData('*', '#', 1, '#', null)]          // invalid table: alternate symbol, no overlay
    [InlineData('/', ' ', 0, '/', null)]          // invalid code: falls back to '/'
    public void ResolveSprite_maps_table_and_overlay(char table, char code, int sheet, char drawn, char? overlay)
    {
        var sprite = AprsSymbol.ResolveSprite(table, code);

        Assert.Equal(sheet, sprite.Sheet);
        Assert.Equal(drawn, sprite.Code);
        Assert.Equal(overlay, sprite.Overlay);
    }

    [Fact]
    public void Lookup_names_an_overlaid_symbol_after_its_alternate_base()
    {
        Assert.Equal(AprsSymbol.Lookup('\\', 'h').Name, AprsSymbol.Lookup('H', 'h').Name);
    }
}
