namespace TravelAgency.Shared.Models;

/// Movimiento del saldo (wallet) de un usuario.
/// `Amount` es positivo cuando suma saldo y negativo cuando lo consume.
public class WalletTransaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public WalletType Type { get; set; }
    public int? BookingId { get; set; }
    public int? RefundId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum WalletType
{
    CancellationCredit = 0,
    BookingPayment = 1,
    PayoutRequest = 2,
    Adjustment = 3
}
