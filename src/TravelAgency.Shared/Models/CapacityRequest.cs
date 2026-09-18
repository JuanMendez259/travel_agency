namespace TravelAgency.Shared.Models;

public class CapacityRequest
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public int UserId { get; set; }
    public int RequestedSeats { get; set; }
    public string? Message { get; set; }
    public bool IsResolved { get; set; }
    public DateTime CreatedAt { get; set; }

    public Trip? Trip { get; set; }
    public User? User { get; set; }
}