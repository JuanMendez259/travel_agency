using System.Text.Json.Serialization;

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
    [JsonIgnore]
    public int? PayoutRequestId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Reembolso asociado. Permite insertar el credito en el mismo SaveChanges
    /// que el BookingRefund (EF resuelve el FK); no se serializa al cliente.</summary>
    [JsonIgnore]
    public BookingRefund? Refund { get; set; }
}

public enum WalletType
{
    CancellationCredit = 0,
    BookingPayment = 1,
    PayoutRequest = 2,
    Adjustment = 3
}

/// Saldo y movimientos del wallet del usuario. Lo deserializa el cliente,
/// por eso vive en Shared junto a WalletTransaction.
public record WalletSummary(decimal Balance, decimal Held, decimal Available, List<WalletTransaction> Transactions);
