namespace TravelAgency.Shared.Models;

public class TripTransport
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public string? ImageUrl { get; set; }
    public string? Plate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public record TripTransportInfo(string? ImageUrl, string? Plate);
