namespace TravelAgency.Shared.Models;

public class TripImagesUploadResult
{
    public Trip Trip { get; set; } = new();
    public List<TripImage> Uploaded { get; set; } = new();
}
