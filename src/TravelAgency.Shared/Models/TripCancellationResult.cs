namespace TravelAgency.Shared.Models;

public class TripCancellationResult
{
    public Trip Trip { get; set; } = new();
    public int BookingsCancelled { get; set; }
    public decimal TotalRefunded { get; set; }
}
