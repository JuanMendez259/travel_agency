using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class ClientTripDetailPage : ContentPage
{
    private readonly ApiService _api;
    private Trip? _trip;

    public string TripId { get; set; } = string.Empty;

    public ClientTripDetailPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_trip is not null) return;

        if (int.TryParse(TripId, out var tripId))
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
            DescriptionLabel.Text = _trip.Description;
            SeatsLabel.Text = $"{_trip.AvailableSeats} asientos disponibles";
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

        BookButton.IsEnabled = false;
        try
        {
            var booking = new Booking
            {
                UserId = 1,
                TripId = _trip.Id,
                NumberOfSeats = seats,
            };

            var created = await _api.CreateBookingAsync(booking);
            await DisplayAlertAsync("Reserva creada",
                $"Tu reserva quedó {created?.Status} por un total de {created?.TotalAmount:C}. Pronto la confirmaremos.",
                "OK");
            await Shell.Current.GoToAsync("..");
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