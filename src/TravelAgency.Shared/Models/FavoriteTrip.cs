namespace TravelAgency.Shared.Models;

public class FavoriteTrip
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int TripId { get; set; }
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
    public Trip? Trip { get; set; }
}