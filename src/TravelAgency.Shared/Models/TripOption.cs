using System.Text.Json.Serialization;

namespace TravelAgency.Shared.Models;

public enum TripOptionCapacityMode
{
    Shared = 0,
    Own = 1
}

public class TripOption
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal PriceAdult { get; set; }
    public decimal? PriceChild { get; set; }
    public int Capacity { get; set; }
    public int AvailableSeats { get; set; }
    public string? Benefits { get; set; }
    public bool IsActive { get; set; }
    public int Order { get; set; }
    public TripOptionCapacityMode CapacityMode { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Marca la opción base del viaje (la "Entrada General"). Siempre existe una por
    /// viaje y no se puede eliminar; el administrador puede desactivarla.
    /// </summary>
    public bool IsBase { get; set; }

    public int EffectiveAvailableSeats => CapacityMode == TripOptionCapacityMode.Own
        ? Math.Max(0, AvailableSeats)
        : Math.Max(0, AvailableSeats);

    [JsonIgnore]
    public Trip? Trip { get; set; }

    [JsonIgnore]
    public ICollection<BookingItem> BookingItems { get; set; } = new List<BookingItem>();

    public List<string> BenefitLines =>
        (Benefits ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0)
            .ToList();
}

public class BookingItem
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public int TripOptionId { get; set; }
    public int Adults { get; set; }
    public int Children { get; set; }
    public decimal UnitPriceAdult { get; set; }
    public decimal? UnitPriceChild { get; set; }
    public string? OptionName { get; set; }

    [JsonIgnore]
    public Booking? Booking { get; set; }

    public TripOption? TripOption { get; set; }

    public int Seats => Adults + Children;
    public decimal LineTotal => Adults * UnitPriceAdult + Children * (UnitPriceChild ?? UnitPriceAdult);
}

public record BookingOptionInput(int TripOptionId, int Adults, int Children);
