using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientMyTripsPage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;

    private List<Booking> _current = new();
    private BookingStatus? _statusFilter;

    public ClientMyTripsPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        SetActivePill(PillTodas);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Title = $"Mis Viajes - {_session.UserName}";
        await LoadMyTripsAsync();
    }

    private BookingStatus? SelectedStatus() => _statusFilter;

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
        var text = SearchEntry.Text?.Trim();
        IEnumerable<Booking> result = _current;

        if (!string.IsNullOrWhiteSpace(text))
        {
            result = result.Where(b =>
                (b.Trip?.Title?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (b.Trip?.Destination?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        MyTripsList.ItemsSource = result.ToList();
    }

    private static readonly Color PillActiveBackground = Color.FromArgb("#512BD4");
    private static readonly Color PillActiveText = Colors.White;
    private static readonly Color PillIdleBackground = Color.FromArgb("#E1E1E1");
    private static readonly Color PillIdleText = Color.FromArgb("#141414");

    private async void OnFilterPillClicked(object? sender, EventArgs e)
    {
        if (sender is not Button clicked || clicked.CommandParameter is not string key) return;

        _statusFilter = key switch
        {
            "pending" => BookingStatus.Pending,
            "confirmed" => BookingStatus.Confirmed,
            "cancelled" => BookingStatus.Cancelled,
            _ => null
        };

        SetActivePill(clicked);
        await LoadMyTripsAsync();
    }

    private void SetActivePill(Button active)
    {
        foreach (var pill in new[] { PillTodas, PillPendientes, PillConfirmadas, PillCanceladas })
        {
            var isActive = pill == active;
            pill.BackgroundColor = isActive ? PillActiveBackground : PillIdleBackground;
            pill.TextColor = isActive ? PillActiveText : PillIdleText;
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