namespace TravelAgency.Shared.Models;

public class TripRating
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public int UserId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string? UserName { get; set; }
    public string? Destination { get; set; }
    public DateTime TripDate { get; set; }
    public TransportType TransportType { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }

    public Trip? Trip { get; set; }
    public User? User { get; set; }
}