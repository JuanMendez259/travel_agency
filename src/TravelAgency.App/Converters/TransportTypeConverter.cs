using System.Globalization;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Converters;

public class TransportTypeConverter : IValueConverter
{
    public static readonly string[] Options = { "Camión", "Camioneta" };

    public static string ToDisplay(TransportType type) =>
        type == TransportType.Camioneta ? "Camioneta" : "Camión";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TransportType type ? ToDisplay(type) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}