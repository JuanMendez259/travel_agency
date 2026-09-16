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

    private async Task LoadBookingsAsync()
    {
        Loading.IsRunning = true;
        Loading.IsVisible = true;
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
            Loading.IsRunning = false;
            Loading.IsVisible = false;
        }
    }

    private async void OnDetailClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Booking booking) return;
        await Shell.Current.GoToAsync($"booking?id={booking.Id}");
    }
}