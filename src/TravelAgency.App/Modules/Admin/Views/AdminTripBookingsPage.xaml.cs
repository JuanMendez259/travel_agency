using TravelAgency.App.Modules.Admin.ViewModels;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminTripBookingsPage : ContentPage
{
    private readonly ApiService _api;

    public string TripId { get; set; } = string.Empty;

    private int _tripId;
    private List<TripBookingItem> _current = new();

    public AdminTripBookingsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
        StatusPicker.ItemsSource = new[] { "Todas", "Pendientes", "Confirmadas", "Canceladas" };
        StatusPicker.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!int.TryParse(TripId, out var id) || id == 0) return;
        _tripId = id;
        await LoadBookingsAsync();
    }

    private BookingStatus? SelectedStatus()
    {
        if (StatusPicker.SelectedIndex < 0) return null;
        return StatusPicker.SelectedIndex switch
        {
            1 => BookingStatus.Pending,
            2 => BookingStatus.Confirmed,
            3 => BookingStatus.Cancelled,
            _ => null
        };
    }

    private async Task LoadBookingsAsync(bool showLoading = true)
    {
        if (_tripId == 0) return;

        if (showLoading)
        {
            Loading.IsRunning = true;
            Loading.IsVisible = true;
        }
        try
        {
            var bookings = await _api.GetTripBookingsAsync(_tripId, SelectedStatus());
            _current = bookings?
                .Select(b => new TripBookingItem(b))
                .ToList() ?? new List<TripBookingItem>();
            ApplySearch();
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

    private void ApplySearch()
    {
        var text = SearchBar.Text?.Trim();
        IEnumerable<TripBookingItem> result = _current;

        if (!string.IsNullOrWhiteSpace(text))
        {
            result = result.Where(item =>
                (item.Client.Contains(text, StringComparison.OrdinalIgnoreCase)) ||
                (item.Booking.Passengers?.Any(p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) ?? false));
        }

        BookingsList.ItemsSource = result.ToList();
    }

    private bool _handlingPicker;
    private async void OnStatusChanged(object? sender, EventArgs e)
    {
        if (_tripId == 0 || _handlingPicker) return;
        _handlingPicker = true;
        try
        {
            await LoadBookingsAsync();
        }
        finally
        {
            _handlingPicker = false;
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplySearch();
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
        if ((sender as Button)?.CommandParameter is not TripBookingItem item) return;
        await Shell.Current.GoToAsync($"booking?id={item.Booking.Id}");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}