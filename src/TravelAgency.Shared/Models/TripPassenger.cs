using System.Text.Json.Serialization;

namespace TravelAgency.Shared.Models;

public class TripPassenger
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public string? Name { get; set; }
    public int? Age { get; set; }
    public bool IsChild { get; set; }
    public string? QrToken { get; set; }
    public bool CheckedIn { get; set; }
    public DateTime? CheckedInAt { get; set; }

    [JsonIgnore]
    public Booking? Booking { get; set; }
}