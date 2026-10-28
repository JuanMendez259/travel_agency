using TravelAgency.App.Services;
using TravelAgency.App.ViewModels;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminTripsPage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private List<TripListItem> _allTrips = new();
    private string _statusFilter = "All";
    private string _searchText = "";

    public AdminTripsPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        StyleChips();
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
                    var thumb = _api.GetTripImage(trip.ImageUrl);
                    items.Add(new TripListItem(trip, thumb, canManage: _session.IsAdmin));
                }
            }
            _allTrips = items;
            ApplyFilters();
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

    private void ApplyFilters()
    {
        IEnumerable<TripListItem> filtered = _allTrips;

        if (_statusFilter == "Active")
            filtered = filtered.Where(i => !i.IsPaused && !i.IsCancelled);
        else if (_statusFilter == "Paused")
            filtered = filtered.Where(i => i.IsPaused);
        else if (_statusFilter == "Cancelled")
            filtered = filtered.Where(i => i.IsCancelled);

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var term = _searchText.Trim();
            filtered = filtered.Where(i =>
                i.Trip.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Trip.Destination.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        TripsList.ItemsSource = filtered.ToList();
    }

    private void OnTripsSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchText = e.NewTextValue ?? "";
        ApplyFilters();
    }

    private void OnTripsStatusChipClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string status) return;
        _statusFilter = status;
        StyleChips();
        ApplyFilters();
    }

    private void StyleChips()
    {
        var (isAll, isActive, isPaused, isCancelled) = (
            _statusFilter == "All",
            _statusFilter == "Active",
            _statusFilter == "Paused",
            _statusFilter == "Cancelled");
        var selectedBackground = GetColor("Primary");
        var selectedText = GetColor("OnPrimary");
        var unselectedBackground = GetColor("SurfaceContainerHigh");
        var unselectedText = GetColor("OnSurface");

        ChipTripsAll.TextColor = isAll ? selectedText : unselectedText;
        ChipTripsAll.BackgroundColor = isAll ? selectedBackground : unselectedBackground;

        ChipTripsActive.TextColor = isActive ? selectedText : unselectedText;
        ChipTripsActive.BackgroundColor = isActive ? selectedBackground : unselectedBackground;

        ChipTripsPaused.TextColor = isPaused ? selectedText : unselectedText;
        ChipTripsPaused.BackgroundColor = isPaused ? selectedBackground : unselectedBackground;

        ChipTripsCancelled.TextColor = isCancelled ? selectedText : unselectedText;
        ChipTripsCancelled.BackgroundColor = isCancelled ? selectedBackground : unselectedBackground;
    }

    private static Color GetColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Transparent;

    private async void OnSeatsClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Trip trip) return;
        await Shell.Current.GoToAsync($"tripseats?id={trip.Id}");
    }

    private async void OnDetailClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Trip trip) return;
        await Shell.Current.GoToAsync($"admintrip?id={trip.Id}");
    }

    private async void OnToggleActiveClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Trip trip) return;

        var pausing = trip.IsActive;
        var confirm = await DisplayAlertAsync(
            pausing ? "Pausar viaje" : "Reactivar viaje",
            pausing
                ? $"¿Pausar \"{trip.Title}\"? Dejará de aparecer para nuevas reservas y se avisará a los clientes con reservas vigentes."
                : $"¿Reactivar \"{trip.Title}\"? Volverá a mostrarse para reservar y se avisará a los clientes con reservas.",
            "Sí", "No");

        if (!confirm) return;

        try
        {
            await _api.UpdateTripActiveAsync(trip.Id, !pausing);
            await LoadTripsAsync(false);
            await DisplayAlertAsync("Listo",
                pausing ? "Viaje pausado. Se notificó a los clientes con reservas." : "Viaje reactivado. Se notificó a los clientes con reservas.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de tu cuenta?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}