namespace TravelAgency.Shared.Models;

public class TripSeatMap
{
    public int TripId { get; set; }
    public string TripTitle { get; set; } = string.Empty;
    public TransportType TransportType { get; set; }
    public int Capacity { get; set; }
    public int OccupiedCount { get; set; }
    public int AvailableCount => Math.Max(0, Capacity - OccupiedCount);
    public List<TripSeatRow> Rows { get; set; } = new();
}

public class TripSeatRow
{
    public int RowNumber { get; set; }
    public List<TripSeat> Seats { get; set; } = new();
}

public class TripSeat
{
    public int Number { get; set; }
    public bool IsAisle { get; set; }
    public bool IsOccupied { get; set; }
    public string? OccupiedBy { get; set; }
    public int? BookingId { get; set; }
    public bool CheckedIn { get; set; }
    public bool IsAssigned { get; set; }

    public string? StatusLabel => IsAisle
        ? null
        : IsOccupied
            ? CheckedIn ? "Ocupado · Check-in hecho" : "Ocupado"
            : "Disponible";
}
