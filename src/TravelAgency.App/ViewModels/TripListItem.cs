using System.ComponentModel;
using System.Runtime.CompilerServices;
using TravelAgency.App.Converters;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.ViewModels;

public class TripListItem : INotifyPropertyChanged
{
    public static Color FavoriteHeartColor => Color.FromArgb("#E53E3E");

    public Trip Trip { get; }
    public Microsoft.Maui.Controls.ImageSource? Thumb { get; }

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeartText));
            OnPropertyChanged(nameof(HeartColor));
        }
    }

    public TripListItem(Trip trip, Microsoft.Maui.Controls.ImageSource? thumb, bool isFavorite = false)
    {
        Trip = trip;
        Thumb = thumb;
        _isFavorite = isFavorite;
    }

    public string HeartText => IsFavorite ? "♥" : "♡";

    public Color HeartColor => IsFavorite ? FavoriteHeartColor : Colors.Gray;

    public int SoldSeats => Trip.Bookings
        .Where(b => b.Status != BookingStatus.Cancelled)
        .Sum(b => b.NumberOfSeats);

    public int AvailableSeats => Math.Max(0, Trip.AvailableSeats);

    public int Capacity => Trip.Capacity;

    public bool HasAvailability => AvailableSeats > 0;

    public bool IsPaused => !Trip.IsActive;

    public string AvailabilityText => HasAvailability
        ? $"{AvailableSeats} asientos disponibles"
        : "Sin cupo";

    public string BookButtonText => HasAvailability ? "Reservar" : "Solicitar cupo";

    public string TransportText => TransportTypeConverter.ToDisplay(Trip.TransportType);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}