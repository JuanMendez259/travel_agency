namespace TravelAgency.Shared.Models;

public class Payment
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public string? TransactionReference { get; set; }

    public Booking? Booking { get; set; }
}

public enum PaymentMethod
{
    CreditCard = 0,
    DebitCard = 1,
    BankTransfer = 2,
    Cash = 3,
    PayPal = 4,
    MercadoPago = 5
}

public enum PaymentStatus
{
    Pending = 0,
    Completed = 1,
    Refunded = 2,
    Failed = 3
}