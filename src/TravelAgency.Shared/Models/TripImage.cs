using System.Text.Json.Serialization;

namespace TravelAgency.Shared.Models;

public class TripImage
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public string Url { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsCover { get; set; }
    public DateTime CreatedAt { get; set; }

    [JsonIgnore]
    public Trip? Trip { get; set; }
}
