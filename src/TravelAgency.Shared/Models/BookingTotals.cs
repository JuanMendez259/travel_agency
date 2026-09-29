namespace TravelAgency.Shared.Models;

/// Totales de dinero de una reserva.
/// `RefundedTotal` tolera reservas canceladas antes de que existiera el reembolso
/// parcial: en ese caso los pagos quedaron marcados como Refunded con el monto completo,
/// asi que se toma `Amount` cuando `RefundedAmount` esta en cero.
public static class BookingTotals
{
    public static decimal PaidTotal(this Booking booking)
        => (booking.Payments ?? Enumerable.Empty<Payment>())
            .Where(p => p.Status == PaymentStatus.Completed)
            .Sum(p => p.Amount);

    public static decimal RefundedTotal(this Booking booking)
        => (booking.Payments ?? Enumerable.Empty<Payment>())
            .Where(p => p.Status == PaymentStatus.Refunded)
            .Sum(p => p.RefundedAmount > 0m ? p.RefundedAmount : p.Amount);

    /// La reserva esta liquidada: lo pagado cubre el total y no fue anulada.
    public static bool IsFullyPaid(this Booking booking)
        => booking.Status != BookingStatus.Cancelled
            && booking.TotalAmount > 0m
            && PaidTotal(booking) >= booking.TotalAmount;

    /// Fue el propio cliente quien la cancelo (no la agencia).
    public static bool WasCancelledByClient(this Booking booking)
        => booking.Status == BookingStatus.Cancelled
            && booking.CancelledAt.HasValue
            && booking.CancelledByUserId == booking.UserId;
}
