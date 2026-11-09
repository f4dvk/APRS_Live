using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AprsLive.Core.Ax25;

/// <summary>A callsign-SSID pair from an AX.25 address field (7 bytes).</summary>
public readonly partial record struct Ax25Address(string Callsign, int Ssid, bool HasBeenRepeated)
{
    /// <summary>Base callsign only, e.g. <c>F4DVK</c> from <c>F4DVK-14</c>.</summary>
    [JsonIgnore]
    public string Base => Callsign;

    public override string ToString() => Ssid == 0 ? Callsign : $"{Callsign}-{Ssid}";

    /// <summary>With a trailing <c>*</c> when the "has-been-repeated" (H) bit is set.</summary>
    public string ToStringWithFlag() => HasBeenRepeated ? ToString() + "*" : ToString();

    /// <summary>
    /// A generic digipeating alias (<c>WIDE2-1</c>, <c>WIDE1*</c>, <c>TRACE</c>,
    /// <c>RELAY</c>...) rather than a station: once used, it doesn't say who used it.
    /// </summary>
    [JsonIgnore]
    public bool IsGenericAlias => GenericAlias().IsMatch(Callsign);

    [GeneratedRegex("^(WIDE|TRACE)[1-7]?$|^(RELAY|GATE|ECHO)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericAlias();

    /// <summary>
    /// Decode one 7-byte address field. Each of the first six bytes is an ASCII
    /// character shifted left by one; the seventh byte carries the SSID (bits
    /// 4..1), the H/C bit (bit 7) and the extension bit (bit 0, set on the last
    /// address of the list).
    /// </summary>
    public static Ax25Address Decode(ReadOnlySpan<byte> field, out bool last)
    {
        Span<char> call = stackalloc char[6];
        int len = 0;
        for (int i = 0; i < 6; i++)
        {
            char c = (char)(field[i] >> 1);
            if (c != ' ')
                call[len++] = c;
        }
        int ssid = (field[6] >> 1) & 0x0F;
        bool h = (field[6] & 0x80) != 0;
        last = (field[6] & 0x01) != 0;
        return new Ax25Address(new string(call[..len]), ssid, h);
    }
}
