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

        TitleLabel.Text = _trip.Title;
        DestinationLabel.Text = _trip.Destination;
        DatesLabel.Text = $"{_trip.StartDate:dd/MM/yyyy} al {_trip.EndDate:dd/MM/yyyy}";
        PriceLabel.Text = _trip.Price.ToString("C");
        if (_trip.ChildPrice.HasValue)
        {
            ChildPriceLabel.Text = $"Niños de 0 a 11 años: {_trip.ChildPrice.Value:C}";
            ChildPriceLabel.IsVisible = true;
        }
        DescriptionLabel.Text = _trip.Description;
        TransportLabel.Text = $"Transporte: {TransportTypeConverter.ToDisplay(_trip.TransportType)}";

        var available = Math.Max(0, _trip.AvailableSeats);
        SeatsLabel.Text = available > 0
            ? $"{available} de {_trip.Capacity} asientos disponibles"
            : $"Sin cupo disponible (capacidad {_trip.Capacity})";
        BookPanel.IsVisible = available > 0;
        NoCapacityPanel.IsVisible = available <= 0;

        if (!string.IsNullOrEmpty(_trip.ImageUrl))
            TripImage.Source = await _api.GetTripImageAsync(_trip.ImageUrl);
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

        if (!int.TryParse(SeatsEntry.Text, out var seats) || seats < 1)
        {
            await DisplayAlertAsync("Error", "Indica un número de asientos válido.", "OK");
            return;
        }

        if (seats > Math.Max(0, _trip.AvailableSeats))
        {
            await DisplayAlertAsync("Error", "No hay suficientes asientos disponibles.", "OK");
            return;
        }

        BookButton.IsEnabled = false;
        try
        {
            if (seats > 1)
            {
                await Shell.Current.GoToAsync($"addpassengers?tripId={_trip.Id}&seats={seats}");
                return;
            }

            var created = await _api.CreateBookingAsync(_trip.Id, 1, null);

            var message = $"Tu reserva quedó {created?.Status} por un total de {created?.TotalAmount:C}. Pronto la confirmaremos.";
            await DisplayAlertAsync("Reserva creada", message, "OK");

            if (created is not null && created.Id > 0)
                await Shell.Current.GoToAsync($"//mytrips/mybooking?id={created.Id}");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            BookButton.IsEnabled = true;
        }
    }
}