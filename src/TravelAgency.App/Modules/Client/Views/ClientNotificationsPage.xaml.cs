using System.Collections.ObjectModel;
using TravelAgency.App.Modules.Client.ViewModels;
using TravelAgency.App.Services;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientNotificationsPage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;

    public ClientNotificationsPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_session.UserId == 0) return;

        try
        {
            var notifications = (await _api.GetNotificationsAsync(_session.UserId))?
                .OrderBy(n => n.CreatedAt)
                .Select(n => new NotificationItem(n))
                .ToList();

            var grouped = notifications?
                .GroupBy(n => n.DateHeaderText)
                .Select(g => new ObservableGroupCollection<string, NotificationItem>(g.Key, g))
                .ToList();

            MessagesList.IsGrouped = true;
            MessagesList.ItemsSource = grouped;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar los avisos: {ex.Message}", "OK");
        }
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        try
        {
            await LoadAsync();
        }
        finally
        {
            MessagesRefresh.IsRefreshing = false;
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
    }
}