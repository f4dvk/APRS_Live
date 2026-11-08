using System.Globalization;
using Avalonia.Data.Converters;
using AprsLive.Core.Settings;

namespace AprsLive.App.Display;

public sealed class StatusModeToLabelConverter : IValueConverter
{
    public static readonly StatusModeToLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        StatusMode.FreeText => "Texte",
        StatusMode.BestDx => "Best DX (station directe la plus lointaine)",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
