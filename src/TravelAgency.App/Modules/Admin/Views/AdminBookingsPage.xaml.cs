using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminBookingsPage : ContentPage
{
    private readonly ApiService _api;

    public AdminBookingsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadBookingsAsync();
    }

    private async Task LoadBookingsAsync(bool showLoading = true)
    {
        if (showLoading)
        {
            Loading.IsRunning = true;
            Loading.IsVisible = true;
        }
        try
        {
            BookingsList.ItemsSource = await _api.GetBookingsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar las reservas: {ex.Message}", "OK");
        }
        finally
        {
            if (showLoading)
            {
                Loading.IsRunning = false;
                Loading.IsVisible = false;
            }
        }
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        try
        {
            await LoadBookingsAsync(false);
        }
        finally
        {
            BookingsRefresh.IsRefreshing = false;
        }
    }

    private async void OnDetailClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Booking booking) return;
        await Shell.Current.GoToAsync($"booking?id={booking.Id}");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}