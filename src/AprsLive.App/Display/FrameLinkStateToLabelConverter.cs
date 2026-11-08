using System.Globalization;
using Avalonia.Data.Converters;
using AprsLive.Core.Links;

namespace AprsLive.App.Display;

public sealed class FrameLinkStateToLabelConverter : IValueConverter
{
    public static readonly FrameLinkStateToLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        FrameLinkState.Disconnected => "Déconnecté",
        FrameLinkState.Connecting => "Connexion…",
        FrameLinkState.Connected => "Connecté",
        FrameLinkState.Faulted => "Erreur",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
