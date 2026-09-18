using TravelAgency.App.Modules.Admin.ViewModels;
using TravelAgency.App.Services;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminTripBookingsPage : ContentPage
{
    private readonly ApiService _api;

    public string TripId { get; set; } = string.Empty;

    public AdminTripBookingsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!int.TryParse(TripId, out var id) || id == 0) return;

        Loading.IsRunning = true;
        Loading.IsVisible = true;
        try
        {
            var bookings = await _api.GetTripBookingsAsync(id);
            BookingsList.ItemsSource = bookings?
                .Select(b => new TripBookingItem(b))
                .ToList();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar las reservas: {ex.Message}", "OK");
        }
        finally
        {
            Loading.IsRunning = false;
            Loading.IsVisible = false;
        }
    }

    private async void OnDetailClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not TripBookingItem item) return;
        await Shell.Current.GoToAsync($"booking?id={item.Booking.Id}");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}