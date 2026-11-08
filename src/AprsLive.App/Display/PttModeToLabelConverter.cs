using System.Globalization;
using Avalonia.Data.Converters;
using AprsLive.Core.Settings;

namespace AprsLive.App.Display;

public sealed class PttModeToLabelConverter : IValueConverter
{
    public static readonly PttModeToLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PttMode.None => "Aucun (VOX)",
        PttMode.Rts => "RTS",
        PttMode.Dtr => "DTR",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
