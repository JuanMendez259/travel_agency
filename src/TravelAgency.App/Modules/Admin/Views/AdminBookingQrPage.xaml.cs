using Microsoft.Maui.Controls;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(BookingId), "id")]
[QueryProperty(nameof(BookingIdFromTrip), "bid")]
[QueryProperty(nameof(PassengerId), "pid")]
public partial class AdminBookingQrPage : ContentPage
{
    private readonly ApiService _api;
    private Booking? _booking;
    private TripPassenger? _passenger;
    private bool _busy;

    public string BookingId { get; set; } = string.Empty;
    public string BookingIdFromTrip { get; set; } = string.Empty;
    public string PassengerId { get; set; } = string.Empty;

    public AdminBookingQrPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadBookingAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar la reserva: {ex.Message}", "OK");
        }
    }

    private async Task LoadBookingAsync()
    {
        var bookingId = int.TryParse(BookingId, out var bid) ? bid
            : int.TryParse(BookingIdFromTrip, out var bid2) ? bid2 : 0;
        if (bookingId <= 0) return;

        _booking = await _api.GetBookingAsync(bookingId);
        if (_booking is null)
        {
            await DisplayAlertAsync("Error", "No se encontró la reserva.", "OK");
            return;
        }

        if (int.TryParse(PassengerId, out var pid))
        {
            _passenger = _booking.Passengers?.FirstOrDefault(p => p.Id == pid);
            RenderPassenger();
        }
        else
        {
            RenderBooking();
        }
    }

    private void RenderPassenger()
    {
        var booking = _booking!;
        var passenger = _passenger;
        var trip = booking.Trip;
        var isCancelled = booking.Status == BookingStatus.Cancelled;

        Title = $"QR · {passenger?.Name}";
        PassengerLabel.Text = passenger?.Name ?? "Pasajero";
        TripLabel.Text = $"{trip?.Title} · Reserva de {booking.User?.Name ?? $"Usuario #{booking.UserId}"}";

        QrImage.Source = QrCodeService.FromToken(passenger?.QrToken);
        QrImage.IsVisible = QrImage.Source is not null;

        SeatsLabel.Text = "Acompañante · 1 asiento";

        if (isCancelled)
            StateLabel.Text = "Reserva cancelada";
        else if (passenger is { CheckedIn: true } && passenger.CheckedInAt.HasValue)
        {
            StateLabel.Text = "Abordó";
            CheckedInAtLabel.Text = $"Hora de abordaje: {passenger.CheckedInAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}";
            CheckedInAtLabel.IsVisible = true;
        }
        else
            StateLabel.Text = "No ha abordado";

        var canToggle = !isCancelled && passenger is not null && trip is not null && !trip.DepartureCompleted;
        BoardButton.Text = passenger is { CheckedIn: true } ? "Quitar abordaje" : "Marcar como abordado";
        BoardButton.IsVisible = canToggle;
    }

    private void RenderBooking()
    {
        var booking = _booking!;
        var trip = booking.Trip;
        var isCancelled = booking.Status == BookingStatus.Cancelled;

        Title = $"QR · {booking.User?.Name ?? $"Usuario #{booking.UserId}"}";
        PassengerLabel.Text = booking.User?.Name ?? booking.User?.Email ?? $"Usuario #{booking.UserId}";
        TripLabel.Text = trip?.Title;

        QrImage.Source = QrCodeService.FromToken(booking.QrToken);
        QrImage.IsVisible = QrImage.Source is not null;

        SeatsLabel.Text = $"{booking.NumberOfSeats} asiento(s) · {booking.TotalAmount:C} · {booking.Status}";

        if (isCancelled)
            StateLabel.Text = "Reserva cancelada";
        else if (booking.CheckedIn && booking.CheckedInAt.HasValue)
        {
            StateLabel.Text = "Abordó";
            CheckedInAtLabel.Text = $"Hora de abordaje: {booking.CheckedInAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}";
            CheckedInAtLabel.IsVisible = true;
        }
        else
            StateLabel.Text = "No ha abordado";

        var canToggle = !isCancelled && trip is not null && !trip.DepartureCompleted;
        BoardButton.Text = booking.CheckedIn ? "Quitar abordaje" : "Marcar como abordado";
        BoardButton.IsVisible = canToggle;
    }

    private async void OnBoardClicked(object? sender, EventArgs e)
    {
        if (_booking is null || _busy) return;

        _busy = true;
        try
        {
            if (_passenger is not null)
            {
                var passengerTarget = !_passenger.CheckedIn;
                var passengerUpdated = await _api.UpdatePassengerCheckinAsync(_passenger.Id, passengerTarget);
                if (passengerUpdated is not null)
                {
                    _passenger.CheckedIn = passengerUpdated.CheckedIn;
                    _passenger.CheckedInAt = passengerUpdated.CheckedInAt;
                }
            }
            else
            {
                var target = !_booking.CheckedIn;
                var updated = await _api.UpdateBookingCheckinAsync(_booking.Id, target);
                if (updated is not null)
                {
                    _booking.CheckedIn = updated.CheckedIn;
                    _booking.CheckedInAt = updated.CheckedInAt;
                }
            }
            if (_passenger is not null)
                RenderPassenger();
            else
                RenderBooking();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            _busy = false;
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}