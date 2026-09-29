using System.Globalization;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Converters;

public class TripDurationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Trip trip) return string.Empty;

        var days = (trip.EndDate.Date - trip.StartDate.Date).Days + 1;
        if (days < 1) days = 1;

        return days == 1 ? "Fin de Semana" : $"{days} días";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
