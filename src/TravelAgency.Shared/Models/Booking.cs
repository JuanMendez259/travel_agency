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

    public User? User { get; set; }
    public Trip? Trip { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public enum BookingStatus
{
    Pending = 0,
    Confirmed = 1,
    Cancelled = 2
}