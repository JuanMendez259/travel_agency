using Microsoft.Maui.Controls;
using Mapsui;
using Mapsui.Layers;
using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(BookingId), "id")]
public partial class ClientMyBookingDetailPage : ContentPage
{
    private readonly ApiService _api;
    private int? _tripId;
    private int _remainingSlots;
    private decimal _cancelRefund;
    private decimal _cancelPenalty;
    private decimal _paidTotal;
    private bool _withinPolicy = true;
    private bool _mapLayersReady;

    public string BookingId { get; set; } = string.Empty;

    public ClientMyBookingDetailPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
        TripMapView.Map ??= new Mapsui.Map();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (int.TryParse(BookingId, out var id))
        {
            try
            {
                await LoadBookingAsync(id);
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", $"No se pudo cargar la reserva: {ex.Message}", "OK");
            }
        }
    }

    private async Task LoadBookingAsync(int id)
    {
        var booking = await _api.GetBookingAsync(id);
        if (booking is null)
        {
            await DisplayAlertAsync("Error", "No se encontró la reserva.", "OK");
            return;
        }

        var trip = booking.Trip;

        Title = trip?.Title;
        TitleLabel.Text = trip?.Title;
        DestinationLabel.Text = trip?.Destination;
        DatesLabel.Text = trip is null
            ? ""
            : $"{trip.StartDate:dd/MM/yyyy HH:mm} al {trip.EndDate:dd/MM/yyyy HH:mm}";
        PriceLabel.Text = trip is null ? "" : $"{trip.Price:C} por asiento";
        DescriptionLabel.Text = trip?.Description;

        if (trip is not null)
        {
            TransportLabel.Text = $"Transporte: {TransportTypeConverter.ToDisplay(trip.TransportType)}";
            AvailabilityLabel.Text = trip.AvailableSeats > 0
                ? $"Disponibles: {Math.Max(0, trip.AvailableSeats)} de {trip.Capacity} asientos"
                : $"Sin cupo disponible (capacidad {trip.Capacity})";

            var pois = trip.PointsOfInterest?
                .OrderBy(p => p.Order)
                .Cast<object>()
                .ToList() ?? new List<object>();
            if (pois.Count > 0)
            {
                ItineraryHeader.IsVisible = true;
                BindableLayout.SetItemsSource(ItineraryLayout, pois);
            }

            await LoadTripMapAsync(trip);
        }

        StatusLabel.Text = $"Estado: {booking.Status}";

        var paid = booking.PaidTotal();
        var refunded = booking.RefundedTotal();
        var total = booking.TotalAmount;
        var remaining = total - paid;

        BalanceLabel.Text = booking.Status == BookingStatus.Cancelled
            ? refunded > 0
                ? $"Reserva cancelada · Reembolsado {refunded:C}"
                : "Reserva cancelada"
            : paid >= total
                ? "Liquidado · ¡Reserva confirmada!"
                : $"Pagado {paid:C} de {total:C} · Saldo pendiente {remaining:C}";

        RenderCancellationSection(booking, trip, paid);

        PaymentsList.ItemsSource = booking.Payments;

        var qrImage = QrCodeService.FromToken(booking.QrToken);
        QrImage.Source = qrImage;
        QrSection.IsVisible = qrImage is not null && booking.Status != BookingStatus.Cancelled;

        var hasPassengers = booking.Passengers is { Count: > 0 };
        PassengersQrButton.IsVisible = hasPassengers && booking.Status != BookingStatus.Cancelled;
        if (hasPassengers)
            PassengersQrButton.Text = $"Ver QR de acompañantes ({booking.Passengers!.Count})";

        var passengerCount = hasPassengers ? booking.Passengers!.Count : 0;
        _remainingSlots = Math.Max(0, booking.NumberOfSeats - 1 - passengerCount);

        if (booking.Status != BookingStatus.Cancelled && booking.NumberOfSeats > 1)
        {
            PassengersListLabel.IsVisible = true;
            PassengersListLabel.Text = hasPassengers
                ? $"Acompañantes: {string.Join(" · ", booking.Passengers!.Select(p => p.Name + (p.IsChild ? " (Niño)" : " (Adulto)")))}"
                : $"Aún no registras acompañantes ({booking.NumberOfSeats - 1} asiento(s) adicionales).";
            AddPassengersButton.IsVisible = _remainingSlots > 0;
            AddPassengersButton.Text = _remainingSlots == 1
                ? "Agregar acompañante"
                : $"Agregar acompañantes (faltan {_remainingSlots})";
        }
        else
        {
            PassengersListLabel.IsVisible = false;
            AddPassengersButton.IsVisible = false;
        }

        PaymentsTotalLabel.Text = booking.Payments is { Count: > 0 }
            ? $"Total abonado: {paid:C}"
            : "";
        PaymentsTotalLabel.IsVisible = booking.Payments is { Count: > 0 };

        await RenderRatingUiAsync(booking, trip);

        if (!string.IsNullOrEmpty(trip?.ImageUrl))
            TripImage.Source = await _api.GetTripImageAsync(trip.ImageUrl);
    }

    private void RenderCancellationSection(Booking booking, Trip? trip, decimal paid)
    {
        var canCancel = trip is not null
            && booking.Status != BookingStatus.Cancelled
            && !trip.DepartureCompleted
            && !trip.Finalized
            && trip.StartDate.Date > DateTime.Today;

        CancelSection.IsVisible = canCancel;
        if (!canCancel) return;

        var limit = trip!.CancellationDaysLimit ?? BookingRefundPolicy.DefaultCancellationDaysLimit;
        var remainingDays = (trip.StartDate.Date - DateTime.Today).Days;
        var withinPolicy = BookingRefundPolicy.IsWithinPolicy(trip.StartDate, DateTime.Today, limit);

        CancelPolicyLabel.Text = withinPolicy
            ? $"Dentro del límite: cancelas con {limit} día(s) o más antes de la salida. Quedan {remainingDays} día(s)."
            : $"Fuera del límite: este viaje exige {limit} día(s) de anticipación y quedan {remainingDays}. Puedes cancelar, pero aplica una multa del 30% sobre lo abonado.";

        // Dentro del limite el reembolso es del 100% de lo abonado; fuera, del 70%.
        _paidTotal = paid;
        _withinPolicy = withinPolicy;
        _cancelPenalty = BookingRefundPolicy.PenaltyFrom(paid, withinPolicy);
        _cancelRefund = BookingRefundPolicy.RefundFrom(paid, withinPolicy);

        CancelRefundLabel.Text = paid <= 0
            ? "No hay pagos abonados: la cancelación no genera reembolso."
            : withinPolicy
                ? $"Sin multa: se reembolsa el 100% de {_paidTotal:C} abonados."
                : $"Multa por cancelación 30%: -{_cancelPenalty:C}. Reembolso: {_cancelRefund:C} de {_paidTotal:C} abonados.";
        CancelButton.IsVisible = true;
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (!int.TryParse(BookingId, out var id) || id <= 0) return;

        string message;
        if (_paidTotal <= 0)
        {
            message = "No hay pagos abonados en esta reserva, así que la cancelación no genera reembolso. ¿Deseas cancelarla?";
        }
        else if (_withinPolicy)
        {
            message = $"Cancelas dentro del límite de días del viaje, así que no hay multa: se reembolsa el 100% de {_paidTotal:C} por tu forma de pago original. Esta acción no puede deshacerse.";
        }
        else
        {
            message = $"Cancelas fuera del límite de días del viaje, así que se aplica una multa del 30%: se retienen {_cancelPenalty:C} de {_paidTotal:C} abonados y se reembolsan {_cancelRefund:C} por tu forma de pago original. Esta acción no puede deshacerse.";
        }

        var confirmed = await DisplayAlertAsync("Cancelar reserva", message, "Cancelar reserva", "Seguir en la reserva");
        if (!confirmed) return;

        CancelButton.IsEnabled = false;
        try
        {
            var result = await _api.CancelBookingAsync(id);
            var refund = result?.RefundAmount ?? 0;
            var success = refund > 0
                ? $"Reserva cancelada. {(result?.WithinPolicy == true ? "Sin multa." : "Multa 30% aplicada.")} Se reembolsarán {refund:C} por tu forma de pago original."
                : "Reserva cancelada. No había pagos que reembolsar.";
            await DisplayAlertAsync("Reserva cancelada", success, "OK");
            await LoadBookingAsync(id);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No fue posible cancelar", ex.Message, "OK");
        }
        finally
        {
            CancelButton.IsEnabled = true;
        }
    }

    private async Task RenderRatingUiAsync(Booking booking, Trip? trip)
    {
        var canRate = trip is not null && booking.Status != BookingStatus.Cancelled && trip.Finalized;
        RateButton.IsVisible = canRate;
        RateHintLabel.IsVisible = canRate;

        if (!canRate)
        {
            _tripId = null;
            return;
        }

        _tripId = trip!.Id;

        var my = await _api.GetMyTripRatingAsync(trip.Id);
        if (my is not null)
        {
            RateButton.Text = "Actualizar mi calificación";
            RateHintLabel.Text = $"Tu calificación: {new string('★', my.Rating)}{new string('☆', 5 - my.Rating)} ({my.Rating}/5)";
        }
        else
        {
            RateButton.Text = "Calificar viaje";
            RateHintLabel.Text = string.Empty;
        }
    }

    private async void OnRateClicked(object? sender, EventArgs e)
    {
        if (_tripId is null) return;
        await Shell.Current.GoToAsync($"ratetrip?tripId={_tripId}");
    }

    private async void OnPassengersQrClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync($"passengersqr?id={BookingId}");
    }

    private async void OnAddPassengersClicked(object? sender, EventArgs e)
    {
        if (int.TryParse(BookingId, out var id) && id > 0 && _remainingSlots > 0)
            await Shell.Current.GoToAsync($"addpassengers?bookingId={id}&count={_remainingSlots}");
    }

    private async Task LoadTripMapAsync(Trip? trip)
    {
        if (trip is null || TripMapView.Map is not { } map) return;

        var markers = new List<MapMarkerInfo>();

        if (trip.OriginLatitude is double olat && trip.OriginLongitude is double olng)
            markers.Add(new MapMarkerInfo(MapMarkerKinds.Origin, 0, olat, olng));

        foreach (var poi in trip.PointsOfInterest.OrderBy(p => p.Order))
            markers.Add(new MapMarkerInfo(MapMarkerKinds.Poi, poi.Id, poi.Latitude, poi.Longitude));

        if (trip.DestinationLatitude is double dlat && trip.DestinationLongitude is double dlng)
            markers.Add(new MapMarkerInfo(MapMarkerKinds.Dest, 0, dlat, dlng));

        if (markers.Count == 0) return;

        if (!_mapLayersReady)
        {
            _mapLayersReady = true;
            TripMapRenderer.AddTileLayer(map);
        }

        map.Layers.Remove(layer => layer is MemoryLayer &&
                                   (layer.Name == "ruta" || layer.Name == "marcadores"));
        map.Layers.Add(TripMapRenderer.BuildRouteLayer(markers), 1);
        map.Layers.Add(TripMapRenderer.BuildMarkersLayer(markers), 2);
        map.Refresh(ChangeType.Discrete);

        TripMapRenderer.FitToMarkers(map, markers);

        MapHeader.IsVisible = true;
        TripMapView.IsVisible = true;
    }
}