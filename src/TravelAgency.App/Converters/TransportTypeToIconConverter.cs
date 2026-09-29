using System.Globalization;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Converters;

public class TransportTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TransportType.Camioneta ? "🚐" : "🚌";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
