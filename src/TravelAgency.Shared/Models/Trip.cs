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

    /// Fecha límite para crear reservas. Si es null, se usa la StartDate.
    public DateTime? BookingDeadline { get; set; }

    /// Categoría comercial del viaje. Si es null, el viaje no tiene categoría.
    public string? Category { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<CapacityRequest> CapacityRequests { get; set; } = new List<CapacityRequest>();
    public ICollection<TripPointOfInterest> PointsOfInterest { get; set; } = new List<TripPointOfInterest>();

    public bool HasOptions { get; set; }

    /// <summary>Indica si el viaje incluye hospedaje.</summary>
    public bool IncludesHotel { get; set; }

    /// <summary>Nombre del hotel. Solo informativo para el administrador.</summary>
    public string? HotelName { get; set; }

    /// <summary>Lugares confirmados con el hotel. Solo aplica si IncludesHotel.</summary>
    public int? HotelCapacity { get; set; }

    /// <summary>Autobus de dos pisos (solo aplica a TransportType.Camion).</summary>
    public bool HasTwoFloors { get; set; }

    /// <summary>Cupo del piso 1 cuando HasTwoFloors. Si no, null.</summary>
    public int? Floor1Capacity { get; set; }

    /// <summary>Cupo del piso 2 cuando HasTwoFloors. Si no, null.</summary>
    public int? Floor2Capacity { get; set; }

    public ICollection<TripOption> Options { get; set; } = new List<TripOption>();

    /// <summary>
    /// Cupo efectivo para reservar: si el viaje incluye hotel se usa el cupo del
    /// hotel (limitado por la capacidad del transporte); si no, la capacidad del transporte.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int BookableCapacity => IncludesHotel && HotelCapacity.HasValue
        ? Math.Min(Capacity, Math.Max(0, HotelCapacity.Value))
        : Capacity;

    [System.Text.Json.Serialization.JsonIgnore]
    public decimal? FromPrice => HasOptions && Options.Count > 0
        ? Options.Where(o => o.IsActive).Min(o => (decimal?)o.PriceAdult) ?? null
        : null;
}

public enum TransportType
{
    Camion = 0,
    Camioneta = 1
}