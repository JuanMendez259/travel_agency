using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminTripDetailPage : ContentPage
{
    private readonly ApiService _api;
    private Trip? _trip;

    public string TripId { get; set; } = string.Empty;

    public AdminTripDetailPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadTripAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar el viaje: {ex.Message}", "OK");
        }
    }

    private async Task LoadTripAsync()
    {
        if (!int.TryParse(TripId, out var id)) return;

        var trips = await _api.GetAdminTripsAsync();
        _trip = trips?.FirstOrDefault(t => t.Id == id);
        if (_trip is null)
        {
            await DisplayAlertAsync("Error", "No se encontró el viaje.", "OK");
            return;
        }

        Title = _trip.Title;
        TitleLabel.Text = _trip.Title;
        DestinationLabel.Text = _trip.Destination;
        DatesLabel.Text = $"{_trip.StartDate:dd/MM/yyyy} al {_trip.EndDate:dd/MM/yyyy}";
        PriceLabel.Text = $"{_trip.Price:C} por asiento";
        TransportLabel.Text = $"Transporte: {TransportTypeConverter.ToDisplay(_trip.TransportType)}";
        StatusLabel.Text = _trip.IsActive ? "Estado: Publicado" : "Estado: Inactivo";
        DescriptionLabel.Text = _trip.Description;

        var sold = _trip.Bookings
            .Where(b => b.Status != BookingStatus.Cancelled)
            .Sum(b => b.NumberOfSeats);

        SoldLabel.Text = sold.ToString();
        AvailableLabel.Text = Math.Max(0, _trip.AvailableSeats).ToString();
        CapacityLabel.Text = _trip.Capacity.ToString();

        RenderCapacityRequests();

        TripImage.Source = await _api.GetTripImageAsync(_trip.ImageUrl);
        TripImage.IsVisible = TripImage.Source is not null;
    }

    private void RenderCapacityRequests()
    {
        var requests = _trip?.CapacityRequests?
            .OrderByDescending(c => c.CreatedAt)
            .ToList() ?? new List<CapacityRequest>();

        CapacityRequestsHeader.IsVisible = requests.Count > 0;
        CapacityRequestsLayout.Children.Clear();

        foreach (var request in requests)
        {
            var name = request.User?.Name ?? request.User?.Email ?? $"Usuario #{request.UserId}";
            var status = request.IsResolved ? " · atendida" : "";
            var text = $"{name} pidió {request.RequestedSeats} asiento(s){status}";
            if (!string.IsNullOrWhiteSpace(request.Message))
                text += $"\n\"{request.Message}\"";

            CapacityRequestsLayout.Children.Add(new Label
            {
                Text = text,
                FontSize = 14,
                TextColor = request.IsResolved ? Colors.Gray : null
            });
        }
    }

    private async void OnPaymentsClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"tripbookings?id={_trip.Id}");
    }

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"//admin?editId={_trip.Id}");
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        var activeBookings = _trip.Bookings
            .Where(b => b.Status is BookingStatus.Pending or BookingStatus.Confirmed)
            .ToList();

        var confirm = await DisplayAlertAsync(
            "Eliminar viaje",
            activeBookings.Count > 0
                ? $"¿Eliminar \"{_trip.Title}\"? Se cancelarán {activeBookings.Count} reservas pendientes o confirmadas y se notificará a esos usuarios."
                : $"¿Eliminar \"{_trip.Title}\"? También se eliminarán sus reservas.",
            "Sí", "No");

        if (!confirm) return;

        string? message = null;
        if (activeBookings.Count > 0)
        {
            message = await DisplayPromptAsync(
                "Mensaje para los afectados",
                $"Los usuarios con reservas pendientes o confirmadas para \"{_trip.Title}\" recibirán este aviso (puedes dejarlo vacío para usar uno automático):",
                accept: "Eliminar y notificar",
                cancel: "Cancelar",
                placeholder: "Tu reserva fue cancelada...");

            if (message is null) return;
        }

        try
        {
            await _api.DeleteTripAsync(_trip.Id, string.IsNullOrWhiteSpace(message) ? null : message);

            if (activeBookings.Count > 0)
                await DisplayAlertAsync("Listo", $"Viaje eliminado. Se notificó a {activeBookings.Count} usuarios con reservas.", "OK");

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}