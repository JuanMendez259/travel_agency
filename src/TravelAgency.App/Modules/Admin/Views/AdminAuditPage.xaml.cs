using Microsoft.Maui.Controls;
using TravelAgency.App.Modules.Admin.ViewModels;
using TravelAgency.App.Services;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(Entity), "entity")]
[QueryProperty(nameof(EntityId), "id")]
public partial class AdminAuditPage : ContentPage
{
    private readonly ApiService _api;
    private bool _loaded;

    public string Entity { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;

    public AdminAuditPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        await LoadLogs();
    }

    private async Task LoadLogs()
    {
        int? entityId = int.TryParse(EntityId, out var parsed) ? parsed : null;

        AuditTitle.Text = Entity.ToLowerInvariant() switch
        {
            "trip" => $"Historial del viaje #{entityId}",
            "booking" => $"Historial de la reserva #{entityId}",
            _ => "Historial de cambios"
        };

        try
        {
            var logs = await _api.GetAuditLogsAsync(Entity, entityId);
            var items = logs?.Select(l => new AuditItem(l)).ToList() ?? new List<AuditItem>();
            BindableLayout.SetItemsSource(AuditList, items);
            EmptyLabel.IsVisible = items.Count == 0;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar el historial: {ex.Message}", "OK");
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}