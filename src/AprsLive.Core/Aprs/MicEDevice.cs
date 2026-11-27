namespace AprsLive.Core.Aprs;

/// <summary>
/// Which device sent a Mic-E packet, from its free text (APRS 1.2): a type
/// byte opens the text and, depending on the make, a version byte or a
/// 2-byte manufacturer/model code closes it.
/// <list type="bullet">
/// <item><c>&gt;</c> and <c>]</c> (Kenwood HTs and mobiles), alone or with one
/// version byte at the end: <c>&gt;...=</c> is a TH-D72, <c>]...=</c> a TM-D710.</item>
/// <item><c>`</c> (messaging) or <c>'</c> (tracker only), then a 2-byte code at the
/// end: <c>`..._0</c> is a Yaesu FT3D, <c>'...|3</c> a Byonics TinyTrak3.</item>
/// <item>A space: the original TAPR Mic-E.</item>
/// </list>
/// Sources: the APRS device identification index kept by OH7LZB for aprs.fi
/// (<see href="https://github.com/aprsorg/aprs-deviceid"/>, CC BY-SA 2.0) and
/// WB4APR's <see href="http://www.aprs.org/aprs12/mic-e-types.txt"/> (SCS modems).
/// </summary>
public static class MicEDevice
{
    /// <summary>Shown for a <c>`</c> type byte whose end code is unknown.</summary>
    public const string GenericMessaging = "Mic-E (messagerie)";

    /// <summary>Shown for a <c>'</c> type byte whose end code is unknown.</summary>
    public const string GenericTracker = "Mic-E (tracker)";

    public const string Original = "Mic-E d'origine";

    // Kenwood: type byte, then an optional version byte at the end. The ones
    // with a version byte come first so they win over the bare type byte.
    private static readonly (char Type, char? Version, string Name)[] Kenwood =
    [
        ('>', '=', "Kenwood TH-D72"),
        ('>', '^', "Kenwood TH-D74"),
        ('>', '&', "Kenwood TH-D75"),
        (']', '=', "Kenwood TM-D710"),
        (']', '&', "Kenwood TM-D750"),
        ('>', null, "Kenwood TH-D7"),
        (']', null, "Kenwood TM-D700"),
    ];

    /// <summary>End codes after a <c>`</c> or <c>'</c> type byte (either one: the type only says whether it takes messages).</summary>
    public static IReadOnlyDictionary<string, string> Codes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Yaesu
        ["_ "] = "Yaesu VX-8",
        ["_\""] = "Yaesu FTM-350",
        ["_#"] = "Yaesu VX-8G",
        ["_$"] = "Yaesu FT1D",
        ["_%"] = "Yaesu FTM-400DR",
        ["_("] = "Yaesu FT2D",
        ["_)"] = "Yaesu FTM-100D",
        ["_0"] = "Yaesu FT3D",
        ["_1"] = "Yaesu FTM-300D",
        ["_2"] = "Yaesu FTM-200D",
        ["_3"] = "Yaesu FT5D",
        ["_4"] = "Yaesu FTM-500D",
        ["_5"] = "Yaesu FTM-510D",
        ["_6"] = "Yaesu FTX-1",
        ["_7"] = "Yaesu FTM-310D",
        // AnyTone
        ["(5"] = "AnyTone AT-D578UV",
        ["(8"] = "AnyTone AT-D878UV",
        // Byonics
        ["|3"] = "Byonics TinyTrak3",
        ["|4"] = "Byonics TinyTrak4",
        // SCS (P4dragon modems)
        [":4"] = "SCS P4dragon DR-7400",
        [":8"] = "SCS P4dragon DR-7800",
        // others
        [":2"] = "SQ8L VP-Tracker",
        [" X"] = "SainSonic AP510",
        ["^A"] = "AxelStack APRS Connect",
        ["^L"] = "F4MLV ESP32 LoRa Tracker",
        ["^v"] = "HinzTec anyfrog",
        ["*v"] = "KissOZ Tracker",
        ["*9"] = "NOR AVRT9",
        ["[1"] = "APRSdroid",
    };

    /// <summary>
    /// The device named by a Mic-E text (what follows the symbol table byte),
    /// or <c>null</c> when the text has no type byte. <paramref name="body"/>
    /// is the text without the bytes recognised here: the type byte, and the
    /// end code once matched. An unknown end code stays in the text, as
    /// WB4APR asks, so the operator can still read it.
    /// </summary>
    public static string? Identify(string text, out string body)
    {
        body = text;
        if (text.Length == 0)
            return null;

        char type = text[0];
        switch (type)
        {
            case '>' or ']':
                foreach (var (t, version, name) in Kenwood)
                {
                    if (t != type)
                        continue;
                    if (version is null)
                    {
                        body = text[1..];
                        return name;
                    }
                    if (text.Length >= 2 && text[^1] == version)
                    {
                        body = text[1..^1];
                        return name;
                    }
                }
                return null;   // not reached: each type has a bare entry

            case '`' or '\'':
                if (text.Length >= 3 && Codes.TryGetValue(text[^2..], out var model))
                {
                    body = text[1..^2];
                    return model;
                }
                body = text[1..];
                return type == '`' ? GenericMessaging : GenericTracker;

            case ' ':
                body = text[1..];
                return Original;

            default:
                return null;
        }
    }
}
