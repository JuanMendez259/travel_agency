namespace TravelAgency.Shared.Models;

/// Solicitud de retiro/pago del saldo del wallet hecha por un usuario.
/// `ResolvedAt` y `ResolvedByUserId` quedan en null mientras esta pendiente.
public class PayoutRequest
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public PayoutStatus Status { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }
}

public enum PayoutStatus
{
    Pending = 0,
    Paid = 1,
    Rejected = 2
}
