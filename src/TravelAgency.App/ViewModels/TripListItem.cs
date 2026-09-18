using TravelAgency.App.Converters;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.ViewModels;

public class TripListItem
{
    public Trip Trip { get; }
    public Microsoft.Maui.Controls.ImageSource? Thumb { get; }

    public TripListItem(Trip trip, Microsoft.Maui.Controls.ImageSource? thumb)
    {
        Trip = trip;
        Thumb = thumb;
    }

    public int SoldSeats => Trip.Bookings
        .Where(b => b.Status != BookingStatus.Cancelled)
        .Sum(b => b.NumberOfSeats);

    public int AvailableSeats => Math.Max(0, Trip.AvailableSeats);

    public int Capacity => Trip.Capacity;

    public bool HasAvailability => AvailableSeats > 0;

    public string AvailabilityText => HasAvailability
        ? $"{AvailableSeats} asientos disponibles"
        : "Sin cupo";

    public string BookButtonText => HasAvailability ? "Reservar" : "Solicitar cupo";

    public string TransportText => TransportTypeConverter.ToDisplay(Trip.TransportType);
}