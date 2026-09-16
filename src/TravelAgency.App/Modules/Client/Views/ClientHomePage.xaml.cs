using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientHomePage : ContentPage
{
    private readonly ApiService _api;

    public ClientHomePage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadTripsAsync();
    }

    private async Task LoadTripsAsync()
    {
        Loading.IsRunning = true;
        Loading.IsVisible = true;
        try
        {
            TripsList.ItemsSource = await _api.GetTripsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar los viajes: {ex.Message}", "OK");
        }
        finally
        {
            Loading.IsRunning = false;
            Loading.IsVisible = false;
        }
    }

    private async void OnBookClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Trip trip) return;
        await Shell.Current.GoToAsync($"trip?id={trip.Id}");
    }
}