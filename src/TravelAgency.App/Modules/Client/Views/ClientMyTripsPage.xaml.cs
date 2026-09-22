using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientMyTripsPage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;

    private List<Booking> _current = new();

    public ClientMyTripsPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        StatusPicker.ItemsSource = new[] { "Todas", "Pendientes", "Confirmadas", "Canceladas" };
        StatusPicker.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Title = $"Mis Viajes - {_session.UserName}";
        await LoadMyTripsAsync();
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

    private async Task LoadMyTripsAsync(bool showLoading = true)
    {
        if (_session.UserId == 0) return;

        if (showLoading)
        {
            Loading.IsRunning = true;
            Loading.IsVisible = true;
        }
        try
        {
            _current = await _api.GetUserBookingsAsync(_session.UserId, SelectedStatus()) ?? new List<Booking>();
            ApplySearch();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar tus viajes: {ex.Message}", "OK");
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
        IEnumerable<Booking> result = _current;

        if (!string.IsNullOrWhiteSpace(text))
        {
            result = result.Where(b =>
                (b.Trip?.Title?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (b.Trip?.Destination?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        MyTripsList.ItemsSource = result.ToList();
    }

    private bool _handlingPicker;
    private async void OnStatusChanged(object? sender, EventArgs e)
    {
        if (_session.UserId == 0 || _handlingPicker) return;
        _handlingPicker = true;
        try
        {
            await LoadMyTripsAsync();
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
            await LoadMyTripsAsync(false);
        }
        finally
        {
            MyTripsRefresh.IsRefreshing = false;
        }
    }

    private async void OnPaymentsClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Booking booking) return;
        await Shell.Current.GoToAsync($"mybooking?id={booking.Id}");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de tu cuenta?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}