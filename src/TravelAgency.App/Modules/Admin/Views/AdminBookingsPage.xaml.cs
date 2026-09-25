using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminBookingsPage : ContentPage
{
    private readonly ApiService _api;
    private BookingStatus? _statusFilter = BookingStatus.Pending;
    private List<Booking> _current = new();
    private DateTime? _fromDate;

    public AdminBookingsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        RenderStatusChips();
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
            _current = await _api.GetBookingsAsync(_statusFilter) ?? new List<Booking>();
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
        IEnumerable<Booking> result = _current;

        if (!string.IsNullOrWhiteSpace(text))
        {
            result = result.Where(b =>
                (b.Trip?.Title?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (b.User?.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (b.User?.Email?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (b.Passengers?.Any(p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) ?? false));
        }

        if (_fromDate.HasValue)
        {
            var from = _fromDate.Value.Date;
            result = result.Where(b => b.Trip?.StartDate.Date >= from);
        }

        BookingsList.ItemsSource = result.ToList();
    }

    private void OnFromDateSelected(object? sender, DateChangedEventArgs e)
    {
        _fromDate = e.NewDate;
        ApplySearch();
    }

    private void OnClearDateClicked(object? sender, EventArgs e)
    {
        _fromDate = null;
        ApplySearch();
    }

    private bool _handlingChip;
    private async void OnStatusChipClicked(object? sender, EventArgs e)
    {
        if (sender is not Button chip || _handlingChip) return;

        _statusFilter = chip.CommandParameter?.ToString() switch
        {
            "Pending" => BookingStatus.Pending,
            "Confirmed" => BookingStatus.Confirmed,
            "Cancelled" => BookingStatus.Cancelled,
            _ => null
        };

        RenderStatusChips();
        _handlingChip = true;
        try
        {
            await LoadBookingsAsync();
        }
        finally
        {
            _handlingChip = false;
        }
    }

    private void RenderStatusChips()
    {
        var defaultColor = Color.FromArgb("#E2E8F0");
        var defaultText = Color.FromArgb("#1A202C");
        var selectedColor = Color.FromArgb("#2B6CB0");
        var selectedText = Colors.White;

        void Style(Button? chip, bool selected)
        {
            if (chip is null) return;
            chip.BackgroundColor = selected ? selectedColor : defaultColor;
            chip.TextColor = selected ? selectedText : defaultText;
        }

        Style(ChipPending, _statusFilter == BookingStatus.Pending);
        Style(ChipConfirmed, _statusFilter == BookingStatus.Confirmed);
        Style(ChipCancelled, _statusFilter == BookingStatus.Cancelled);
        Style(ChipAll, _statusFilter is null);
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
        if ((sender as Button)?.CommandParameter is not Booking booking) return;
        await Shell.Current.GoToAsync($"booking?id={booking.Id}");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}