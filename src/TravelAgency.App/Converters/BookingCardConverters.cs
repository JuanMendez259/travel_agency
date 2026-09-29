using System.Globalization;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Converters;

/// Reglas compartidas por los indicadores de pago de la tarjeta de Mis Viajes.
public static class BookingCardRules
{
    /// Ventana de aviso previa a la fecha limite de reserva.
    public static readonly TimeSpan Notice = TimeSpan.FromHours(24);

    /// Misma regla que usa la API y el detalle del viaje: si no hay fecha limite
    /// explicita, la fecha de salida es el limite.
    public static DateTime? EffectiveDeadline(Booking booking)
        => booking.Trip is { } trip ? trip.BookingDeadline ?? trip.StartDate : null;

    /// true si la reserva sigue pendiente de pago y su limite vence dentro de 24 h.
    public static bool IsPaymentUrgent(Booking booking)
    {
        if (booking.Status != BookingStatus.Pending) return false;
        if (EffectiveDeadline(booking) is not { } limit) return false;

        var remaining = limit - DateTime.Now;
        return remaining > TimeSpan.Zero && remaining <= Notice;
    }

    /// Lo que el cliente ha pagado hasta ahora. Solo cuenta lo completado:
    /// se excluyen pagos pendientes, fallidos y refunded.
    public static decimal PaidAmount(Booking booking)
        => booking.PaidTotal();

    public static string SeatsText(int seats)
        => seats == 1 ? "1 lugar reservado" : $"{seats} lugares reservados";
}

/// Texto de aviso cuando la fecha limite esta por vencer.
/// Devuelve null cuando no aplica, para poder reutilizar HasValueConverter como visibilidad.
public class BookingPaymentUrgentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Booking booking || !BookingCardRules.IsPaymentUrgent(booking)) return null;

        return $"Tienes 24 hrs para confirmar tu pago y asegurar tus " +
               BookingCardRules.SeatsText(booking.NumberOfSeats);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// Visibilidad del badge "Expira Pronto".
public class BookingExpiringSoonConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Booking booking && BookingCardRules.IsPaymentUrgent(booking);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// "Anticipo $1,500.00 MXN" con formato fijo, sin depender del locale del dispositivo.
public class BookingAnticipoConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Booking booking) return string.Empty;

        return $"Anticipo ${BookingCardRules.PaidAmount(booking):#,##0.00} MXN";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// Visibilidad del boton "Completar Pago": solo en reservas pendientes de pago.
public class BookingNeedsPaymentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Booking booking && booking.Status == BookingStatus.Pending;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
/// "Pagado 100%" cuando la reserva esta liquidada: lo pagado cubre el total.
/// Mismo criterio que usa el detalle de la reserva ("Liquidado").
public class BookingFullyPaidConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Booking booking && booking.IsFullyPaid();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// Aviso de cancelacion. Depende de la bandera que deja el servidor: si la
/// cancelo el propio cliente lo dice, y si fue la agencia avisa que contacte
/// a la agencia. Devuelve null cuando la reserva no esta cancelada.
public class BookingCancellationNoticeConverter : IValueConverter
{
    public const string ByClient =
        "Cancelado por el usuario. Solicita tu reembolso por el medio que realizaste el pago original.";

    public const string ByAgency =
        "Cancelada por la agencia. Contacta a la agencia para conocer tus opciones de reembolso.";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Booking { Status: BookingStatus.Cancelled } booking) return null;

        return booking.WasCancelledByClient() ? ByClient : ByAgency;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
