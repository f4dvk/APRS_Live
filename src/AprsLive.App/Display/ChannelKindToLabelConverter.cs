using System.Globalization;
using Avalonia.Data.Converters;
using AprsLive.Core.Settings;

namespace AprsLive.App.Display;

public sealed class ChannelKindToLabelConverter : IValueConverter
{
    public static readonly ChannelKindToLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ChannelKind.KissSerial => "KISS série",
        ChannelKind.KissTcp => "KISS TCP",
        ChannelKind.AudioAfsk => "Audio (AFSK1200)",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
