namespace TravelAgency.Shared.Models;

public class Booking
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int TripId { get; set; }
    public DateTime BookingDate { get; set; }
    public BookingStatus Status { get; set; }
    public int NumberOfSeats { get; set; }
    public decimal TotalAmount { get; set; }

    public bool CheckedIn { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public string? QrToken { get; set; }

    public User? User { get; set; }
    public Trip? Trip { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<TripPassenger> Passengers { get; set; } = new List<TripPassenger>();
}

public enum BookingStatus
{
    Pending = 0,
    Confirmed = 1,
    Cancelled = 2
}

public record CancelBookingResult(Booking Booking, decimal RefundAmount);