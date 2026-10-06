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

    public string BookingId { get; set; } = string.Empty;

    public ClientMyBookingDetailPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
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

        var currentBooking = booking ?? throw new InvalidOperationException("No se encontró la reserva.");
        var trip = currentBooking.Trip;

        Title = trip?.Title;
        TitleLabel.Text = trip?.Title;
        DestinationLabel.Text = trip?.Destination;
        if (FindByName("SalidaLabel") is Label sl && trip is not null)
            sl.Text = $"{trip.StartDate:dddd dd/MM/yyyy HH:mm}";
        if (FindByName("RegresoLabel") is Label rl && trip is not null)
            rl.Text = $"{trip.EndDate:dddd dd/MM/yyyy HH:mm}";


        if (DurationLabel is not null && trip is not null)
        {
            var days = (trip.EndDate.Date - trip.StartDate.Date).TotalDays;
            var nights = Math.Max(0, days - 1);
            DurationLabel.Text = $"{days} {(days == 1 ? "Día" : "Días")} / {nights} {(nights == 1 ? "Noche" : "Noches")}";
        }
        if (ItineraryOutLabel is not null && trip is not null)
            ItineraryOutLabel.Text = $"{trip.StartDate:dddd dd/MM/yyyy HH:mm}";
        if (ItineraryBackLabel is not null && trip is not null)
            ItineraryBackLabel.Text = $"{trip.EndDate:dddd dd/MM/yyyy HH:mm}";
        OutMeetingLabel.Text = "Punto de salida: por confirmar";
        BackMeetingLabel.Text = "Punto de regreso: por confirmar";


        DescriptionLabel.Text = trip?.Description;

            var pois = trip?.PointsOfInterest?
                .OrderBy(p => p.Order)
                .Cast<object>()
                .ToList() ?? new List<object>();
            if (pois.Count > 0)
            {
                ItineraryHeader.IsVisible = true;
                BindableLayout.SetItemsSource(ItineraryLayout, pois);
            }

        StatusLabel.Text = $"Estado: {currentBooking.Status}";

        var paid = currentBooking.PaidTotal();
        var refunded = currentBooking.RefundedTotal();
        var total = currentBooking.TotalAmount;
        var remaining = total - paid;
        var payments = currentBooking.Payments?.OrderBy(p => p.PaymentDate).ToList() ?? new List<Payment>();
        var passengers = currentBooking.Passengers?.ToList() ?? new List<TripPassenger>();

        if (ReservaCupoLabel is not null)
            ReservaCupoLabel.Text = $"{currentBooking.NumberOfSeats} lugar(es)";
        if (ReservaTotalLabel is not null)
            ReservaTotalLabel.Text = $"{currentBooking.TotalAmount:C}";
        if (ReservaIncluyeLabel1 is not null)
            ReservaIncluyeLabel1.Text = "Próximamente";
        if (ReservaIncluyeLabel2 is not null)
            ReservaIncluyeLabel2.IsVisible = false;
        BalanceLabel!.Text = currentBooking.Status == BookingStatus.Cancelled
            ? refunded > 0
                ? $"Reserva cancelada · Reembolsado {refunded:C}"
                : "Reserva cancelada"
            : paid >= total
                ? "Liquidado · ¡Reserva confirmada!"
                : $"Pagado {paid:C} de {total:C} · Saldo pendiente {remaining:C}";

        RenderCancellationSection(currentBooking, trip, paid);
        if (FindByName("ReservaCupoLabel") is Label rcl)
            rcl.Text = $"Cupo reservado: {currentBooking.NumberOfSeats} lugar(es)";
        if (FindByName("ReservaTotalLabel") is Label rtl)
            rtl.Text = $"Monto Total: {currentBooking.TotalAmount:C}";

        var saldoPendiente = currentBooking.TotalAmount - paid;
        if (PagoSaldoLabel is not null)
            PagoSaldoLabel!.Text = saldoPendiente > 0 ? $"Saldo Pendiente: {saldoPendiente:C}" : "Saldo Pendiente: $0.00 MXN";
        if (PagoAbono1Label is not null)
        {
            if (payments.Count > 0)
                PagoAbono1Label.Text = $"{payments[0].PaymentDate:dd MMM} - {payments[0].Amount:C}";
            else
                PagoAbono1Label.Text = "Próximamente";
        }
        if (PagoAbono2Label is not null)
        {
            if (payments.Count > 1)
            {
                PagoAbono2Label.IsVisible = true;
                PagoAbono2Label.Text = $"{payments[1].PaymentDate:dd MMM} - {payments[1].Amount:C}";
            }
            else
            {
                PagoAbono2Label.IsVisible = false;
            }
        }

        if (FindByName("PagoSaldoLabel") is Label psl)
        {
            var saldoPendienteFind = currentBooking.TotalAmount - paid;
            psl.Text = saldoPendienteFind > 0 ? $"Saldo Pendiente: {saldoPendienteFind:C}" : "Saldo Pendiente: $0.00 MXN";
        }
        if (FindByName("PagoAbono1Label") is Label pa1)
        {
            if (payments.Count > 0)
                pa1.Text = $"{payments[0].PaymentDate:dd MMM} - {payments[0].Amount:C}";
            else
                pa1.Text = "Próximamente";
        }
        if (FindByName("PagoAbono2Label") is Label pa2)
        {
            if (payments.Count > 1)
            {
                pa2.IsVisible = true;
                pa2.Text = $"{payments[1].PaymentDate:dd MMM} - {payments[1].Amount:C}";
            }
            else
            {
                pa2.IsVisible = false;
            }
        }
        BindableLayout.SetItemsSource(PaymentsLayout, payments);
        NoPaymentsLabel.IsVisible = payments.Count == 0;
        if (currentBooking.HasOptionItems && currentBooking.Items is not null && currentBooking.Items.Count > 0)
        {
            ItemsSection.IsVisible = true;
            ItemsLayout.Children.Clear();
            foreach (var it in currentBooking.Items)
            {
                var optName = it.TripOption?.Name ?? $"Opción #{it.TripOptionId}";
                var row = new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    Padding = new Thickness(0, 4, 0, 4)
                };
                row.Add(new Label { Text = $"{optName} · {it.Adults}A/{it.Children}N", FontSize = 12, TextColor = Color.FromArgb("#CCFFFFFF") }, 0, 0);
                row.Add(new Label { Text = it.LineTotal.ToString("C"), FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#CCFFFFFF"), HorizontalOptions = LayoutOptions.End }, 1, 0);
                ItemsLayout.Children.Add(row);
            }
        }


        var qrImage = QrCodeService.FromToken(currentBooking.QrToken);
        QrImage!.Source = qrImage;
        QrSection!.IsVisible = qrImage is not null && currentBooking.Status != BookingStatus.Cancelled;

        var hasPassengers = passengers.Count > 0;
        PassengersQrButton.IsVisible = hasPassengers && currentBooking.Status != BookingStatus.Cancelled;
        if (hasPassengers)
            PassengersQrButton.Text = $"Ver QR de acompañantes ({passengers.Count})";

        var passengerCount = passengers.Count;
        _remainingSlots = Math.Max(0, booking.NumberOfSeats - 1 - passengerCount);

        if (booking.Status != BookingStatus.Cancelled && booking.NumberOfSeats > 1)
        {
            PassengersListLabel.IsVisible = true;
            PassengersListLabel.Text = hasPassengers
                ? $"Acompañantes: {string.Join(" · ", passengers.Select(p => p.Name + (p.IsChild ? " (Niño)" : " (Adulto)")))}"
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

        PaymentsTotalLabel.Text = payments.Count > 0
            ? $"Total abonado: {paid:C}"
            : "";
        PaymentsTotalLabel.IsVisible = payments.Count > 0;

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

    private async void OnMapRouteClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_tripId is not int tripId || tripId <= 0) return;
            await Shell.Current.GoToAsync($"clienttripmap?tripId={tripId}");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnForceMapClicked(object? sender, EventArgs e)
    {
        try
        {
            var tripId = _tripId ?? 8;
            await Shell.Current.GoToAsync($"clienttripmap?tripId={tripId}");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }
}