namespace TravelAgency.Shared.Models;

public class TripPointOfInterest
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Order { get; set; }

    public Trip? Trip { get; set; }
}