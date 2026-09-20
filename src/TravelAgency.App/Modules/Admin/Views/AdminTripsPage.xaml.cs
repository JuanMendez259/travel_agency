using TravelAgency.App.Services;
using TravelAgency.App.ViewModels;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminTripsPage : ContentPage
{
    private readonly ApiService _api;

    public AdminTripsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadTripsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar los viajes: {ex.Message}", "OK");
        }
    }

    private async Task LoadTripsAsync(bool showLoading = true)
    {
        if (showLoading)
        {
            Loading.IsRunning = true;
            Loading.IsVisible = true;
        }
        try
        {
            var trips = await _api.GetAdminTripsAsync();
            var items = new List<TripListItem>();
            if (trips is not null)
            {
                foreach (var trip in trips)
                {
                    var thumb = await _api.GetTripImageAsync(trip.ImageUrl);
                    items.Add(new TripListItem(trip, thumb));
                }
            }
            TripsList.ItemsSource = items;
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
            await LoadTripsAsync(false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar los viajes: {ex.Message}", "OK");
        }
        finally
        {
            TripsRefresh.IsRefreshing = false;
        }
    }

    private async void OnDetailClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Trip trip) return;
        await Shell.Current.GoToAsync($"admintrip?id={trip.Id}");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de tu cuenta?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}