using TravelAgency.App.Services;
using TravelAgency.App.ViewModels;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientFavoritesPage : ContentPage
{
    private readonly ApiService _api;

    public ClientFavoritesPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadFavoritesAsync();
    }

    private async Task LoadFavoritesAsync(bool showLoading = true)
    {
        if (showLoading)
        {
            Loading.IsRunning = true;
            Loading.IsVisible = true;
        }
        try
        {
            var trips = await _api.GetFavoritesAsync();
            var items = new List<TripListItem>();
            if (trips is not null)
            {
                foreach (var trip in trips)
                {
                    var thumb = await _api.GetTripImageAsync(trip.ImageUrl);
                    items.Add(new TripListItem(trip, thumb, isFavorite: true));
                }
            }
            FavoritesList.ItemsSource = items;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar tus favoritos: {ex.Message}", "OK");
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
            await LoadFavoritesAsync(false);
        }
        finally
        {
            FavoritesRefresh.IsRefreshing = false;
        }
    }

    private async void OnTripClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Trip trip) return;
        await Shell.Current.GoToAsync($"trip?id={trip.Id}");
    }

    private async void OnFavoriteClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not TripListItem item) return;
        try
        {
            item.IsFavorite = await _api.RemoveFavoriteAsync(item.Trip.Id);
            await LoadFavoritesAsync(false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }
}