using System.Globalization;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class ClientTripDetailPage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private Trip? _trip;
    private bool _isFavorite;
    private int _adults = 1;
    private int _children;
    private int _availableSeats;
    private bool _bookingClosed;

    public string TripId { get; set; } = string.Empty;

    public ClientTripDetailPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_trip is not null) return;

        if (int.TryParse(TripId, out var tripId))
        {
            try
            {
                await LoadTripAsync(tripId);
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", $"No se pudo cargar el viaje: {ex.Message}", "OK");
            }
        }
    }

    private async Task LoadTripAsync(int tripId)
    {
        _trip = await _api.GetTripAsync(tripId);
        if (_trip is null)
        {
            await DisplayAlertAsync("Error", "No se encontró el viaje.", "OK");
            return;
        }

        try
        {
            var favorites = await _api.GetFavoritesAsync();
            SetFavorite(favorites?.Any(t => t.Id == tripId) == true);
        }
        catch
        {
            SetFavorite(false);
        }

        TitleLabel.Text = _trip.Title;
        DestinationLabel.Text = _trip.Destination;
        DescriptionShortLabel.Text = _trip.Description;
        AdultPriceLabel.Text = _trip.Price.ToString("C");
        ChildPriceLabel.Text = _trip.ChildPrice?.ToString("C") ?? string.Empty;
        ChildrenRow.IsVisible = _trip.ChildPrice.HasValue;
        DescriptionLabel.Text = _trip.Description;
        CategoryLabel.Text = _trip.Category;
        CategoryLabel.IsVisible = !string.IsNullOrWhiteSpace(_trip.Category);
        TransportLabel.Text = TransportTypeConverter.ToDisplay(_trip.TransportType);
        CapacityTitleLabel.Text = $"Capacidad: {TransportTypeConverter.ToDisplay(_trip.TransportType)}";

        var available = Math.Max(0, _trip.AvailableSeats);
        SeatsLabel.Text = $"{_trip.Capacity - available} confirmados";
        SeatsLeftLabel.Text = $"Quedan {available} de {_trip.Capacity} asientos";
        UrgentLabel.Text = available > 0 && available <= 3 ? $"⚡ Últimos {available} lugares" : string.Empty;
        UrgentLabel.IsVisible = available > 0 && available <= 3;
        CapacityBar.Progress = _trip.Capacity > 0
            ? Math.Clamp((double)(_trip.Capacity - available) / _trip.Capacity, 0, 1)
            : 0;

        var deadline = _trip.BookingDeadline ?? _trip.StartDate;
        var bookingClosed = DateTime.Today > deadline.Date;
        DeadlineLabel.Text = bookingClosed
            ? $"Reservas cerradas · {deadline:dd/MM/yyyy}"
            : $"Reservas abiertas hasta {deadline:dd/MM/yyyy}";
        DeadlineLabel.IsVisible = true;

        var days = (_trip.EndDate.Date - _trip.StartDate.Date).Days + 1;
        var nights = Math.Max(0, days - 1);
        DurationLabel.Text = $"{days} {(days == 1 ? "Día" : "Días")} / {nights} {(nights == 1 ? "Noche" : "Noches")}";

        ItineraryOutLabel.Text = $"{FormatTripDateTime(_trip.StartDate)}";
        OutMeetingLabel.Text = $"Punto de encuentro: {_trip.Destination}";
        ItineraryBackLabel.Text = $"{FormatTripDateTime(_trip.EndDate)}";
        BackMeetingLabel.Text = $"Llegada al mismo punto de partida ({_trip.Destination}).";

        _availableSeats = available;
        _bookingClosed = bookingClosed;
        BookPanel.IsVisible = available > 0;
        NoCapacityPanel.IsVisible = available <= 0;
        BookClosedLabel.Text = $"Este viaje cerró reservas el {deadline:dd/MM/yyyy}.";
        BookClosedLabel.IsVisible = available > 0 && bookingClosed;
        UpdateBookingSummary();

        if (!string.IsNullOrEmpty(_trip.ImageUrl))
            TripImage.Source = await _api.GetTripImageAsync(_trip.ImageUrl);
    }

    private static string FormatTripDateTime(DateTime value)
    {
        var culture = CultureInfo.GetCultureInfo("es-MX");
        var date = value.ToString("ddd d MMM", culture);
        var hour = value.ToString("HH:mm");
        var meridiem = value.Hour < 12 ? "AM" : "PM";
        return $"{char.ToUpper(date[0])}{date[1..]} • {hour} {meridiem}";
    }

    private void UpdateBookingSummary()
    {
        var adults = _adults;
        var children = _children;
        var total = adults * (_trip?.Price ?? 0) + children * (_trip?.ChildPrice ?? 0);
        var seats = adults + children;

        AdultsCountLabel.Text = adults.ToString();
        ChildrenCountLabel.Text = children.ToString();

        var maxSeats = Math.Max(0, _availableSeats);
        AdultsPlusButton.IsEnabled = !_bookingClosed && seats < maxSeats;
        ChildrenPlusButton.IsEnabled = !_bookingClosed && seats < maxSeats && (_trip?.ChildPrice).HasValue;
        AdultsMinusButton.IsEnabled = adults > 1;
        ChildrenMinusButton.IsEnabled = children > 0;
        BookButton.IsEnabled = !_bookingClosed && seats >= 1 && seats <= maxSeats;

        TotalLabel.Text = total.ToString("C");
        SeatsSelectionHintLabel.Text = seats == 1
            ? "(1 asiento seleccionado)"
            : $"({seats} asientos seleccionados)";
    }

    private void OnAdultsMinusClicked(object? sender, EventArgs e)
    {
        if (_adults > 1) _adults--;
        UpdateBookingSummary();
    }

    private void OnAdultsPlusClicked(object? sender, EventArgs e)
    {
        if (_adults + _children < Math.Max(0, _availableSeats)) _adults++;
        UpdateBookingSummary();
    }

    private void OnChildrenMinusClicked(object? sender, EventArgs e)
    {
        if (_children > 0) _children--;
        UpdateBookingSummary();
    }

    private void OnChildrenPlusClicked(object? sender, EventArgs e)
    {
        if (_adults + _children < Math.Max(0, _availableSeats)) _children++;
        UpdateBookingSummary();
    }

    private void SetFavorite(bool isFavorite)
    {
        _isFavorite = isFavorite;
        FavoriteButton.Text = isFavorite ? "♥" : "♡";
        FavoriteButton.TextColor = isFavorite ? Color.FromArgb("#E53E3E") : Colors.Gray;
    }

    private async void OnFavoriteClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        try
        {
            var nowFavorite = _isFavorite
                ? await _api.RemoveFavoriteAsync(_trip.Id)
                : await _api.AddFavoriteAsync(_trip.Id);
            SetFavorite(nowFavorite);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = _trip.Title,
                Text = $"{_trip.Title} · {_trip.Destination}\n" +
                       $"Del {_trip.StartDate:dd/MM/yyyy HH:mm} al {_trip.EndDate:dd/MM/yyyy HH:mm}\n" +
                       $"Precio por persona: {_trip.Price:C}"
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo compartir", ex.Message, "OK");
        }
    }

    private async void OnRequestCapacityClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        if (!int.TryParse(RequestedSeatsEntry.Text, out var seats) || seats < 1)
        {
            await DisplayAlertAsync("Error", "Indica cuántos asientos necesitas.", "OK");
            return;
        }

        RequestCapacityButton.IsEnabled = false;
        try
        {
            await _api.CreateCapacityRequestAsync(_trip.Id, seats, RequestMessageEditor.Text?.Trim());
            await DisplayAlertAsync("Solicitud enviada",
                $"Le avisaremos cuando se habilite más cupo para \"{_trip.Title}\".", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            RequestCapacityButton.IsEnabled = true;
        }
    }

    private async void OnBookClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        var seats = _adults + _children;
        if (seats < 1)
        {
            await DisplayAlertAsync("Error", "Selecciona al menos un asiento.", "OK");
            return;
        }

        if (seats > Math.Max(0, _trip.AvailableSeats))
        {
            await DisplayAlertAsync("Error", "No hay suficientes asientos disponibles.", "OK");
            return;
        }

        await Shell.Current.GoToAsync($"bookseats?tripId={_trip.Id}&seats={seats}");
    }
}