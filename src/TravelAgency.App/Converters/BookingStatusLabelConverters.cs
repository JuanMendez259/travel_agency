using System.Globalization;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Converters;

// Complementan los convertidores existentes (TripDuration, Anticipo, FullyPaid, etc.).
// No reemplazan ni duplican la lógica de pagos existente.

public class BookingStatusToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            BookingStatus.Confirmed => "Confirmado",
            BookingStatus.Pending => "Pago pendiente",
            BookingStatus.Cancelled => "Cancelado",
            _ => "Sin estado"
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BookingStatusToPillColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            BookingStatus.Confirmed => Color.FromArgb("#DFF7ED"),
            BookingStatus.Pending => Color.FromArgb("#FEF3C7"),
            BookingStatus.Cancelled => Color.FromArgb("#FFDAD6"),
            _ => Color.FromArgb("#EAEDFF")
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BookingStatusToPillTextColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            BookingStatus.Confirmed => Color.FromArgb("#00714D"),
            BookingStatus.Pending => Color.FromArgb("#78350F"),
            BookingStatus.Cancelled => Color.FromArgb("#93000A"),
            _ => Color.FromArgb("#404941")
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}