namespace TravelAgency.Shared.Models;

/// Politica de reembolso por cancelacion.
/// Multa del 30% por cada boleto reservado: el cliente recupera el 70% de lo abonado.
/// El boleto se prorratea sobre el total de la reserva.
/// La multa se calcula sobre lo efectivamente pagado, no sobre el total pendiente:
/// si solo se abono un anticipo, la penalizacion aplica solo a ese anticipo.
/// Fuente unica de verdad: la usan la API y la app para que no diverjan.
public static class BookingRefundPolicy
{
    public const decimal PenaltyRate = 0.30m;

    /// Precio de un boleto = total de la reserva / asientos reservados.
    public static decimal TicketPrice(Booking booking)
        => booking.NumberOfSeats > 0
            ? booking.TotalAmount / booking.NumberOfSeats
            : booking.TotalAmount;

    /// Parte pagada de un boleto, para desglosar el aviso al cliente.
    public static decimal PaidTicketPrice(Booking booking, decimal paid)
        => booking.NumberOfSeats > 0 ? paid / booking.NumberOfSeats : paid;

    /// Multa total sobre lo abonado. Si solo hay anticipo, la multa es sobre el anticipo.
    public static decimal PenaltyFrom(decimal paid)
        => Math.Round(paid * PenaltyRate, 2);

    /// A devolver al cliente: lo abonado menos la multa, nunca negativo.
    public static decimal RefundFrom(decimal paid)
        => Math.Round(paid - PenaltyFrom(paid), 2);

    public static decimal PenaltyFor(Booking booking, decimal paid)
        => PenaltyFrom(paid);

    public static decimal RefundFor(Booking booking, decimal paid)
        => RefundFrom(paid);
}
