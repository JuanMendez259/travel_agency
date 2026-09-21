using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminTripDetailPage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private Trip? _trip;

    public string TripId { get; set; } = string.Empty;

    public AdminTripDetailPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;

        if (_session.IsCoordinator)
        {
            PaymentsButton.IsVisible = false;
            MapButton.IsVisible = false;
            CheckinButton.IsVisible = false;
            AuditButton.IsVisible = false;
            EditActions.IsVisible = false;
        }
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
        RenderPassengers();

        TripImage.Source = await _api.GetTripImageAsync(_trip.ImageUrl);
        TripImage.IsVisible = TripImage.Source is not null;
    }

    private void RenderPassengers()
    {
        var bookings = _trip?.Bookings?
            .OrderByDescending(b => b.BookingDate)
            .ToList() ?? new List<Booking>();

        PassengersHeader.IsVisible = bookings.Count > 0;
        PassengersTotalLabel.IsVisible = bookings.Count > 0;
        PassengersLayout.Children.Clear();

        foreach (var booking in bookings)
        {
            var name = booking.User?.Name ?? booking.User?.Email ?? $"Usuario #{booking.UserId}";
            var isCancelled = booking.Status == BookingStatus.Cancelled;

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                },
                Padding = new Thickness(0, 3)
            };

            var nameLabel = new Label
            {
                Text = name,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center
            };
            var seatsLabel = new Label
            {
                Text = $"{booking.NumberOfSeats} asiento(s) · {booking.TotalAmount:C}",
                FontSize = 13,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            var statusLabel = new Label
            {
                Text = booking.Status.ToString(),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextColor = booking.Status switch
                {
                    BookingStatus.Confirmed => Color.FromArgb("#2F855A"),
                    BookingStatus.Pending => Color.FromArgb("#B7791F"),
                    _ => Colors.Gray
                }
            };

            Grid.SetColumn(seatsLabel, 1);
            Grid.SetColumn(statusLabel, 2);
            row.Add(nameLabel);
            row.Add(seatsLabel);
            row.Add(statusLabel);

            PassengersLayout.Children.Add(row);
        }

        var nonCancelled = bookings.Where(b => b.Status != BookingStatus.Cancelled).ToList();
        var totalSeats = nonCancelled.Sum(b => b.NumberOfSeats);
        var totalAmount = nonCancelled.Sum(b => b.TotalAmount);

        PassengersHeader.Text = $"Pasajeros ({bookings.Count})";
        PassengersTotalLabel.Text = $"Total: {totalSeats} asiento(s) vendidos · {totalAmount:C}";
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

    private async void OnMapClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"tripmap?id={_trip.Id}");
    }

    private async void OnAuditClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"auditlog?entity=Trip&id={_trip.Id}");
    }

    private async void OnCheckinClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"checkin?id={_trip.Id}");
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
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de tu cuenta?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}