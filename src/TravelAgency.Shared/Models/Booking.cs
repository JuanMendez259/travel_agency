namespace TravelAgency.Shared.Models;

public class Booking
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int TripId { get; set; }
    public DateTime BookingDate { get; set; }
    public BookingStatus Status { get; set; }
    public int NumberOfSeats { get; set; }
    public int? SeatNumber { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>Codigo de descuento aplicado (snapshot; null si no hubo).</summary>
    public string? DiscountCode { get; set; }

    /// <summary>Monto descontado (snapshot). El subtotal se deriva: TotalAmount + DiscountAmount.</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>Comentario del cliente sobre necesidades especiales para el viaje (null si no aplica).</summary>
    public string? SpecialNeedsNote { get; set; }

    public bool CheckedIn { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public string? QrToken { get; set; }

    /// Auditoria de la cancelacion. `CancelledByUserId` guarda quien cancelo
    /// (el cliente o el administrador), lo que permite distinguir una cancelacion
    /// hecha desde la app de una hecha por la agencia.
    public DateTime? CancelledAt { get; set; }
    public int? CancelledByUserId { get; set; }

    public User? User { get; set; }
    public Trip? Trip { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<TripPassenger> Passengers { get; set; } = new List<TripPassenger>();

    public ICollection<BookingItem> Items { get; set; } = new List<BookingItem>();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasOptionItems => Items.Count > 0;
}

public enum BookingStatus
{
    Pending = 0,
    Confirmed = 1,
    Cancelled = 2
}

public record CancelBookingResult(Booking Booking, decimal RefundAmount, decimal PenaltyAmount, bool WithinPolicy);