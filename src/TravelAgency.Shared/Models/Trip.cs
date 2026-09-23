namespace TravelAgency.Shared.Models;

public class Trip
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Destination { get; set; }
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal Price { get; set; }
    public decimal? ChildPrice { get; set; }
    public int Capacity { get; set; }
    public int AvailableSeats { get; set; }
    public TransportType TransportType { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public double? OriginLatitude { get; set; }
    public double? OriginLongitude { get; set; }
    public double? DestinationLatitude { get; set; }
    public double? DestinationLongitude { get; set; }

    public bool CheckInOpen { get; set; }
    public bool DepartureCompleted { get; set; }
    public bool Finalized { get; set; }

    public int? CancellationDaysLimit { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<CapacityRequest> CapacityRequests { get; set; } = new List<CapacityRequest>();
    public ICollection<TripPointOfInterest> PointsOfInterest { get; set; } = new List<TripPointOfInterest>();
}

public enum TransportType
{
    Camion = 0,
    Camioneta = 1
}