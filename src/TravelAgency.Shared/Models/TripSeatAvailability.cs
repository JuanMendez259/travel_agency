namespace TravelAgency.Shared.Models;

public class TripSeatAvailability
{
    public int TripId { get; set; }
    public string TripTitle { get; set; } = string.Empty;
    public TransportType TransportType { get; set; }
    public int Capacity { get; set; }
    public int OccupiedCount { get; set; }
    public int AvailableCount => Math.Max(0, Capacity - OccupiedCount);
    public List<int> OccupiedSeats { get; set; } = new();
    public List<TripSeatRow> Rows { get; set; } = new();
}
