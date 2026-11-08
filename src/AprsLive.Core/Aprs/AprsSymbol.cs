namespace AprsLive.Core.Aprs;

/// <summary>
/// APRS symbol (table + code) → a human name and a coarse category. Used to
/// pick a marker glyph on the map and to populate the station icon picker.
/// Only the symbols that matter most in the field are named here; everything
/// else falls back to <see cref="Category.Generic"/>. The full APRS 1.01
/// symbol set can be dropped in later (Phase 3, icon picker) without
/// touching callers.
/// </summary>
public static class AprsSymbol
{
    public enum Category
    {
        Generic,
        Person,
        Vehicle,
        Emergency,
        Aircraft,
        Boat,
        House,
        Station,
        Digipeater,
        Weather,
        Object
    }

    public readonly record struct Info(string Name, Category Category);

    // key: two chars "<table><code>", table '/' = primary, '\' = alternate
    private static readonly Dictionary<string, Info> Map = new()
    {
        ["/!"] = new("Poste de police", Category.Emergency),
        ["/#"] = new("Digipeater", Category.Digipeater),
        ["/&"] = new("Passerelle / gateway", Category.Station),
        ["/'"] = new("Petit aéronef", Category.Aircraft),
        ["/-"] = new("Maison (station fixe)", Category.House),
        ["/>"] = new("Voiture", Category.Vehicle),
        ["/["] = new("Personne / piéton", Category.Person),
        ["/b"] = new("Vélo", Category.Vehicle),
        ["/f"] = new("Camion de pompiers", Category.Emergency),
        ["/g"] = new("Planeur", Category.Aircraft),
        ["/j"] = new("Jeep", Category.Vehicle),
        ["/k"] = new("Camion", Category.Vehicle),
        ["/p"] = new("Chien", Category.Generic),
        ["/s"] = new("Bateau / navire", Category.Boat),
        ["/u"] = new("Camion (semi)", Category.Vehicle),
        ["/v"] = new("Van", Category.Vehicle),
        ["/y"] = new("Station Yagi QTH", Category.Station),
        ["/O"] = new("Ballon", Category.Aircraft),
        ["/U"] = new("Bus", Category.Vehicle),
        ["/X"] = new("Hélicoptère", Category.Aircraft),
        ["/Y"] = new("Voilier", Category.Boat),
        ["/a"] = new("Ambulance", Category.Emergency),
        ["/_"] = new("Station météo", Category.Weather),
        ["/r"] = new("Pylône relais (repeater tower)", Category.Digipeater),
        ["\\!"] = new("Urgence (EMERGENCY!)", Category.Emergency),
        ["\\+"] = new("Croix Rouge", Category.Emergency),
        ["\\-"] = new("Maison (panneaux solaires)", Category.House),
        ["\\/"] = new("Repère / waypoint", Category.Object),
        ["\\O"] = new("Rocket / ballon", Category.Aircraft),
        ["\\a"] = new("Boîte / dépôt", Category.Object),
        ["\\h"] = new("Hôpital", Category.Emergency),
        ["\\s"] = new("Power boat", Category.Boat),
        ["\\u"] = new("Camion (alternatif)", Category.Vehicle),
    };

    public static Info Lookup(char table, char code)
    {
        // An overlay (0-9, A-Z) sits on an alternate-table symbol.
        char t = ResolveSprite(table, code).Sheet == 0 ? '/' : '\\';
        return Map.TryGetValue($"{t}{code}", out var info)
            ? info
            : new Info($"Symbole {t}{code}", Category.Generic);
    }

    /// <summary>
    /// How to draw a symbol from the hessu/aprs-symbols sheets: <see cref="Sheet"/>
    /// 0 = primary table, 1 = alternate table, and an optional overlay character
    /// (0-9, A-Z) drawn on top from the overlay sheet.
    /// </summary>
    public readonly record struct Sprite(int Sheet, char Code, char? Overlay);

    /// <summary>
    /// APRS 1.01: '/' is the primary table, '\' the alternate one, and any
    /// 0-9 / A-Z in the table position is an overlay on the alternate symbol
    /// (compressed positions write the digits 0-9 as a-j).
    /// </summary>
    public static Sprite ResolveSprite(char table, char code)
    {
        char c = code is < '!' or > '~' ? '/' : code;
        if (table == '/')
            return new Sprite(0, c, null);
        if (table is >= 'a' and <= 'j')
            table = (char)('0' + (table - 'a'));
        bool overlay = table is >= '0' and <= '9' or >= 'A' and <= 'Z';
        return new Sprite(1, c, overlay ? table : null);
    }

    /// <summary>One named symbol, for a picker list: table, code, and its name.</summary>
    public readonly record struct Entry(char Table, char Code, string Name)
    {
        public override string ToString() => $"{Table}{Code} — {Name}";
    }

    /// <summary>
    /// The curated, named symbols (see <see cref="Map"/>), sorted by name — enough
    /// for a station-icon picker. Not the full APRS 1.01 symbol table (191×2
    /// entries); unnamed symbols are reachable by typing table/code directly.
    /// </summary>
    public static IReadOnlyList<Entry> KnownSymbols { get; } = Map
        .Select(kv => new Entry(kv.Key[0], kv.Key[1], kv.Value.Name))
        .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
}
