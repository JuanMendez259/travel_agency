using TravelAgency.App.Services;
using TravelAgency.App.ViewModels;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientHomePage : ContentPage
{
    private readonly ApiService _api;
    private List<TripListItem> _items = new();

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

    private async Task LoadTripsAsync(bool showLoading = true)
    {
        if (showLoading)
        {
            Loading.IsRunning = true;
            Loading.IsVisible = true;
        }
        try
        {
            var trips = await _api.GetTripsAsync();

            var favIds = new HashSet<int>();
            try
            {
                var favorites = await _api.GetFavoritesAsync();
                favIds = favorites?.Select(t => t.Id).ToHashSet() ?? new HashSet<int>();
            }
            catch
            {
                // los favoritos no deben impedir ver la lista de viajes
            }

            var items = new List<TripListItem>();
            if (trips is not null)
            {
                foreach (var trip in trips)
                {
                    var thumb = await _api.GetTripImageAsync(trip.ImageUrl);
                    items.Add(new TripListItem(trip, thumb, favIds.Contains(trip.Id)));
                }
            }
            _items = items;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar los viajes: {ex.Message}", "OK");
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

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var text = SearchEntry.Text?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            TripsList.ItemsSource = _items;
            TripsList.EmptyView = "No hay viajes disponibles.";
            return;
        }

        var matches = _items
            .Where(i => Matches(i.Trip.Title, text)
                     || Matches(i.Trip.Destination, text)
                     || Matches(i.Trip.Category, text)
                     || Matches(i.Trip.Description, text))
            .ToList();

        TripsList.ItemsSource = matches;
        TripsList.EmptyView = matches.Count == 0
            ? $"Sin resultados para \"{text}\"."
            : "No hay viajes disponibles.";
    }

    private static bool Matches(string? value, string text)
        => value is not null && value.Contains(text, StringComparison.CurrentCultureIgnoreCase);

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        try
        {
            await LoadTripsAsync(false);
        }
        finally
        {
            TripsRefresh.IsRefreshing = false;
        }
    }

    private async void OnBookClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Trip trip) return;
        await Shell.Current.GoToAsync($"trip?id={trip.Id}");
    }

    private async void OnFavoriteClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not TripListItem item) return;
        try
        {
            item.IsFavorite = item.IsFavorite
                ? await _api.RemoveFavoriteAsync(item.Trip.Id)
                : await _api.AddFavoriteAsync(item.Trip.Id);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnFavoritesClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("favorites");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de tu cuenta?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}