namespace TravelAgency.Shared.Models;

/// Politica de reembolso por cancelacion.
/// Dentro del limite de dias del viaje (CancellationDaysLimit) el reembolso es del 100% de lo abonado.
/// Fuera del limite se aplica una multa del 30% sobre lo abonado y se devuelve el 70%.
/// El boleto se prorratea sobre el total de la reserva.
/// La multa se calcula sobre lo efectivamente pagado, no sobre el total pendiente:
/// si solo se abono un anticipo, la penalizacion aplica solo a ese anticipo.
/// Fuente unica de verdad: la usan la API y la app para que no diverjan.
public static class BookingRefundPolicy
{
    public const decimal PenaltyRate = 0.30m;

    /// Dias por defecto cuando el viaje no tiene limite configurado.
    public const int DefaultCancellationDaysLimit = 3;

    /// Dias que faltan para la salida segun el limite de cancelacion del viaje.
    /// Sin limite configurado no hay anticipacion minima: siempre dentro de politica.
    public static bool IsWithinPolicy(DateTime startDate, DateTime today, int? cancellationDaysLimit)
        => cancellationDaysLimit is not int limit || (startDate.Date - today.Date).Days >= limit;

    /// Precio de un boleto = total de la reserva / asientos reservados.
    public static decimal TicketPrice(Booking booking)
        => booking.NumberOfSeats > 0
            ? booking.TotalAmount / booking.NumberOfSeats
            : booking.TotalAmount;

    /// Parte pagada de un boleto, para desglosar el aviso al cliente.
    public static decimal PaidTicketPrice(Booking booking, decimal paid)
        => booking.NumberOfSeats > 0 ? paid / booking.NumberOfSeats : paid;

    /// Multa total sobre lo abonado. Dentro de la politica no hay multa.
    public static decimal PenaltyFrom(decimal paid, bool withinPolicy)
        => withinPolicy ? 0m : Math.Round(paid * PenaltyRate, 2);

    /// A devolver al cliente: dentro de la politica el 100% de lo abonado,
    /// fuera de la politica lo abonado menos la multa, nunca negativo.
    public static decimal RefundFrom(decimal paid, bool withinPolicy)
        => Math.Round(paid - PenaltyFrom(paid, withinPolicy), 2);

    public static decimal PenaltyFor(Booking booking, decimal paid, bool withinPolicy)
        => PenaltyFrom(paid, withinPolicy);

    public static decimal RefundFor(Booking booking, decimal paid, bool withinPolicy)
        => RefundFrom(paid, withinPolicy);
}
