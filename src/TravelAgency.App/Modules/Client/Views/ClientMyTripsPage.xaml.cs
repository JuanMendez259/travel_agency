using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientMyTripsPage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;

    public ClientMyTripsPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Title = $"Mis Viajes - {_session.UserName}";
        await LoadMyTripsAsync();
    }

    private async Task LoadMyTripsAsync()
    {
        if (_session.UserId == 0) return;

        Loading.IsRunning = true;
        Loading.IsVisible = true;
        try
        {
            MyTripsList.ItemsSource = await _api.GetUserBookingsAsync(_session.UserId);

            var notifications = await _api.GetNotificationsAsync(_session.UserId);
            if (notifications is { Count: > 0 })
            {
                NotificationsList.ItemsSource = notifications;
                NotificationsSection.IsVisible = true;
            }
            else
            {
                NotificationsSection.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar tus viajes: {ex.Message}", "OK");
        }
        finally
        {
            Loading.IsRunning = false;
            Loading.IsVisible = false;
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