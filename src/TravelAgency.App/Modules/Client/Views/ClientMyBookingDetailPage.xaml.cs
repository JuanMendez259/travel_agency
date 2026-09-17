using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(BookingId), "id")]
public partial class ClientMyBookingDetailPage : ContentPage
{
    private readonly ApiService _api;

    public string BookingId { get; set; } = string.Empty;

    public ClientMyBookingDetailPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (int.TryParse(BookingId, out var id))
        {
            var booking = await _api.GetBookingAsync(id);
            if (booking is null)
            {
                await DisplayAlertAsync("Error", "No se encontró la reserva.", "OK");
                return;
            }

            TripLabel.Text = booking.Trip?.Title;
            DestinationLabel.Text = booking.Trip?.Destination;
            DatesLabel.Text = $"{booking.Trip?.StartDate:dd/MM/yyyy} al {booking.Trip?.EndDate:dd/MM/yyyy}";
            InfoLabel.Text = $"{booking.NumberOfSeats} asientos · Total {booking.TotalAmount:C}";
            StatusLabel.Text = $"Estado: {booking.Status}";
            PaymentsList.ItemsSource = booking.Payments;
        }
    }
}