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
    private readonly List<BookingItem> _bookingItems = new();
    private int? _tripId;
    private int _remainingSlots;
    private decimal _cancelRefund;
    private decimal _cancelPenalty;
    private decimal _paidTotal;
    private decimal _pendingAmount;
    private decimal _walletAvailable;
    private bool _isCancelled;
    private bool _withinPolicy = true;
    private bool _tripHasOptions;
    private bool _canCancel;

    public string BookingId { get; set; } = string.Empty;

    public ClientMyBookingDetailPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    // Resuelve un token de color global (Colors.xaml) con un respaldo seguro.
    private static Color Token(string key, string fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Color.FromArgb(fallback);

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

        _tripHasOptions = trip?.HasOptions ?? false;
        _bookingItems.Clear();
        if (currentBooking.Items is not null)
            _bookingItems.AddRange(currentBooking.Items);

        // Id del viaje para el boton de mapa (siempre disponible, aunque la reserva no este finalizada).
        _tripId = trip?.Id;
        ForceMapButton.IsVisible = _tripId is not null;

        Title = trip?.Title;
        TitleLabel.Text = trip?.Title;
        DestinationLabel.Text = trip?.Destination;


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

        if (currentBooking.DiscountAmount > 0)
        {
            ReservaDescuentoTitle.IsVisible = true;
            ReservaDescuentoLabel.IsVisible = true;
            ReservaDescuentoLabel.Text = $"-{currentBooking.DiscountAmount:C} ({currentBooking.DiscountCode})";
        }
        else
        {
            ReservaDescuentoTitle.IsVisible = false;
            ReservaDescuentoLabel.IsVisible = false;
        }

        var specialNeeds = currentBooking.SpecialNeedsNote?.Trim();
        SpecialNeedsSection.IsVisible = !string.IsNullOrEmpty(specialNeeds);
        SpecialNeedsLabel.Text = specialNeeds ?? string.Empty;
        if (currentBooking.Status == BookingStatus.Cancelled)
        {
            // Las cancelaciones con BookingRefund muestran su reembolso historico.
            // Las anteriores al wallet solo tienen pagos marcados como Refunded.
            BalanceLabel!.Text = currentBooking.RefundTotal > 0
                ? $"Saldo a favor: {currentBooking.RefundTotal:C}"
                : $"Reserva cancelada · Reembolsado {refunded:C}";
        }
        else
        {
            BalanceLabel!.Text = paid >= total
                ? "Liquidado · ¡Reserva confirmada!"
                : $"Pagado {paid:C} de {total:C} · Saldo pendiente {remaining:C}";
        }

        RenderCancellationSection(currentBooking, trip, paid);
        RenderPassengersCancelSection(passengers);

        var saldoPendiente = currentBooking.TotalAmount - paid;
        if (PagoSaldoLabel is not null)
            PagoSaldoLabel.Text = saldoPendiente > 0 ? $"Saldo Pendiente: {saldoPendiente:C}" : "Saldo Pendiente: $0.00 MXN";

        _pendingAmount = saldoPendiente;
        _isCancelled = currentBooking.Status == BookingStatus.Cancelled;
        await RefreshPaySectionAsync();

        BindableLayout.SetItemsSource(PaymentsLayout, payments);
        NoPaymentsLabel.IsVisible = payments.Count == 0;
        // Solo tiene sentido desglosar cuando la reserva abarca mas de una opcion distinta.
        var distinctOptions = currentBooking.Items?.Select(i => i.TripOptionId).Distinct().Count() ?? 0;
        if (distinctOptions >= 2)
        {
            ItemsSection.IsVisible = true;
            ItemsLayout.Children.Clear();
            var optionTextColor = Token("OnSurface", "#131B2E");
            var benefitTextColor = Token("OnSurfaceVariant", "#404941");
            var subCardBg = Token("SurfaceContainerLow", "#F2F3FF");
            var subCardStroke = Token("SurfaceContainerHigh", "#E2E7FF");
            var secondaryColor = Token("Secondary", "#006C49");

            foreach (var it in currentBooking.Items!)
            {
                var optName = it.OptionName ?? it.TripOption?.Name ?? $"Opción #{it.TripOptionId}";
                var sub = new VerticalStackLayout { Spacing = 4 };

                var header = new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    ColumnSpacing = 8
                };
                header.Add(new Label { Text = optName, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = optionTextColor }, 0, 0);
                header.Add(new Label { Text = it.LineTotal.ToString("C"), FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = optionTextColor, HorizontalOptions = LayoutOptions.End }, 1, 0);
                sub.Add(header);

                sub.Add(new Label { Text = $"{it.Adults}A / {it.Children}N", FontSize = 12, TextColor = benefitTextColor });

                var benefits = it.TripOption?.BenefitLines ?? new List<string>();
                if (benefits.Count > 0)
                {
                    sub.Add(new Label { Text = "Incluye:", FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = secondaryColor, Margin = new Thickness(0, 2, 0, 0) });
                    foreach (var benefit in benefits)
                        sub.Add(new Label { Text = $"• {benefit}", FontSize = 12, TextColor = benefitTextColor });
                }

                ItemsLayout.Children.Add(new Border
                {
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                    Stroke = subCardStroke,
                    StrokeThickness = 1,
                    BackgroundColor = subCardBg,
                    Padding = new Thickness(12),
                    Content = sub
                });
            }
        }
        else
        {
            ItemsSection.IsVisible = false;
        }


        var qrImage = QrCodeService.FromToken(currentBooking.QrToken);
        QrImage!.Source = qrImage;
        QrSection!.IsVisible = qrImage is not null && currentBooking.Status != BookingStatus.Cancelled;

        HolderSeatLabel.Text = $"{(currentBooking.User?.Name ?? "Titular")}\nAsiento Reservado: {SeatLabels.Format(trip, currentBooking.SeatNumber)}";

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

        _canCancel = canCancel;
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

    // Muestra, cuando la reserva aun se puede cancelar, una fila por acompanante
    // con la accion de cancelar su boleto individual.
    private void RenderPassengersCancelSection(List<TripPassenger> passengers)
    {
        PassengersCancelLayout.Children.Clear();

        var visible = _canCancel && passengers.Count > 0;
        PassengersCancelSection.IsVisible = visible;
        if (!visible) return;

        var nameColor = Token("OnSurface", "#131B2E");
        var metaColor = Token("OnSurfaceVariant", "#404941");
        var rowBg = Token("SurfaceContainerLow", "#F2F3FF");
        var strokeColor = Token("SurfaceContainerHigh", "#E2E7FF");
        var errorColor = Token("Error", "#BA1A1A");

        foreach (var passenger in passengers)
        {
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 10
            };

            var avatar = new Border
            {
                WidthRequest = 38,
                HeightRequest = 38,
                StrokeThickness = 0,
                BackgroundColor = rowBg,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                VerticalOptions = LayoutOptions.Center
            };
            avatar.Content = new Label
            {
                Text = passenger.IsChild ? "🧒" : "🧑",
                FontSize = 18,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            row.Add(avatar, 0, 0);

            var info = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
            info.Add(new Label
            {
                Text = string.IsNullOrWhiteSpace(passenger.Name) ? "Acompañante" : passenger.Name,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = nameColor
            });
            info.Add(new Label
            {
                Text = passenger.SeatNumber is int seat
                    ? $"Asiento {seat} · {(passenger.IsChild ? "Niño" : "Adulto")}"
                    : (passenger.IsChild ? "Niño" : "Adulto"),
                FontSize = 11,
                TextColor = metaColor
            });
            row.Add(info, 1, 0);

            var cancelButton = new Button
            {
                Text = "Cancelar boleto",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                BackgroundColor = Colors.Transparent,
                TextColor = errorColor,
                BorderColor = errorColor,
                BorderWidth = 1,
                CornerRadius = 10,
                HeightRequest = 38,
                Padding = new Thickness(12, 0),
                CommandParameter = passenger,
                VerticalOptions = LayoutOptions.Center
            };
            cancelButton.Clicked += OnCancelTicketClicked;
            row.Add(cancelButton, 2, 0);

            PassengersCancelLayout.Children.Add(new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Stroke = strokeColor,
                StrokeThickness = 1,
                BackgroundColor = rowBg,
                Padding = new Thickness(10),
                Content = row
            });
        }
    }

    private async Task RefreshPaySectionAsync()
    {
        var show = _pendingAmount > 0 && !_isCancelled;
        PaySection.IsVisible = show;
        if (!show) return;

        PayPendingLabel.Text = $"Saldo pendiente: {_pendingAmount:C}";

        try
        {
            var wallet = await _api.GetWalletAsync();
            _walletAvailable = wallet?.Available ?? 0m;
        }
        catch
        {
            _walletAvailable = 0m;
        }

        PayAvailableLabel.Text = _walletAvailable > 0
            ? $"Saldo disponible: {_walletAvailable:C}"
            : "No tienes saldo disponible para pagar esta reserva.";
        RealizarPagoButton.IsEnabled = _walletAvailable > 0;
    }

    private async void OnRealizarPagoClicked(object? sender, EventArgs e)
    {
        if (!int.TryParse(BookingId, out var id) || id <= 0) return;
        if (_pendingAmount <= 0) return;

        if (_walletAvailable <= 0)
        {
            await DisplayAlertAsync("Realizar pago",
                "No tienes saldo disponible para pagar esta reserva.", "OK");
            return;
        }

        var cap = Math.Min(_walletAvailable, _pendingAmount);
        var input = await DisplayPromptAsync(
            "Realizar pago",
            $"Saldo disponible: {_walletAvailable:C}\nSaldo pendiente: {_pendingAmount:C}\n¿Cuanto deseas pagar?",
            accept: "Pagar con saldo",
            cancel: "Cancelar",
            placeholder: "Monto en MXN",
            keyboard: Keyboard.Numeric,
            initialValue: cap.ToString("0.##"));
        if (string.IsNullOrWhiteSpace(input)) return;

        if (!decimal.TryParse(input, out var amount) || amount <= 0)
        {
            await DisplayAlertAsync("Monto invalido", "Escribe un monto mayor a cero.", "OK");
            return;
        }

        if (amount > cap)
        {
            await DisplayAlertAsync("Monto invalido",
                $"El monto supera el maximo permitido de {cap:C}.", "OK");
            return;
        }

        RealizarPagoButton.IsEnabled = false;
        try
        {
            await _api.PayWithWalletAsync(id, amount);
            await DisplayAlertAsync("Pago realizado",
                $"Aplicamos {amount:C} de tu saldo a favor a esta reserva.", "OK");
            await LoadBookingAsync(id);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo pagar", ex.Message, "OK");
            RealizarPagoButton.IsEnabled = true;
        }
    }

    private static string BookingItemChoiceLabel(BookingItem item)
    {
        var name = item.OptionName ?? item.TripOption?.Name ?? $"Opción #{item.TripOptionId}";
        return $"{name} · {item.Adults}A/{item.Children}N · {item.LineTotal:C}";
    }

    private async void OnCancelTicketClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not TripPassenger passenger) return;
        if (!int.TryParse(BookingId, out var id) || id <= 0) return;

        var reason = await DisplayPromptAsync(
            "Cancelar boleto",
            $"Indica el motivo para cancelar el boleto de {passenger.Name ?? "este acompañante"}:",
            accept: "Continuar",
            cancel: "Cancelar",
            placeholder: "Describe el motivo");
        if (string.IsNullOrWhiteSpace(reason)) return;

        // Con varias lineas de opciones hay que elegir la entrada del boleto.
        int? bookingItemId = null;
        if (_tripHasOptions && _bookingItems.Count > 1)
        {
            // Numerar cada opcion para que la etiqueta sea unica: el ActionSheet solo
            // devuelve el string pulsado, asi que dos entradas con el mismo texto
            // colisionarian y se elegiria el boleto equivocado.
            var labels = _bookingItems.Select((it, i) => $"{i + 1}) {BookingItemChoiceLabel(it)}").ToArray();
            var selected = await DisplayActionSheetAsync("Selecciona la entrada", "Cancelar", null, labels);
            if (string.IsNullOrEmpty(selected) || selected == "Cancelar") return;

            var index = Array.IndexOf(labels, selected);
            if (index < 0 || index >= _bookingItems.Count) return;
            bookingItemId = _bookingItems[index].Id;
        }

        button.IsEnabled = false;
        try
        {
            var result = await _api.CancelTicketAsync(id, passenger.Id, bookingItemId, reason!);
            var refund = result?.RefundAmount ?? 0m;
            var balance = result?.WalletBalance ?? 0m;
            await DisplayAlertAsync(
                "Boleto cancelado",
                $"Se canceló el boleto de {passenger.Name ?? "el acompañante"}. Reembolso a tu saldo: {refund:C}. Saldo a favor actual: {balance:C}.",
                "OK");
            await LoadBookingAsync(id);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No fue posible cancelar", ex.Message, "OK");
            button.IsEnabled = true;
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (!int.TryParse(BookingId, out var id) || id <= 0) return;

        var reason = await DisplayPromptAsync(
            "Cancelar reserva",
            "Indica el motivo de la cancelación de la reserva:",
            accept: "Continuar",
            cancel: "Cancelar",
            placeholder: "Describe el motivo");
        if (string.IsNullOrWhiteSpace(reason)) return;

        string message;
        if (_paidTotal <= 0)
        {
            message = "No hay pagos abonados en esta reserva, así que la cancelación no genera reembolso. ¿Deseas cancelarla?";
        }
        else if (_withinPolicy)
        {
            message = $"Cancelas dentro del límite de días del viaje, así que no hay multa: se reembolsa el 100% de {_paidTotal:C} a tu saldo a favor. Esta acción no puede deshacerse.";
        }
        else
        {
            message = $"Cancelas fuera del límite de días del viaje, así que se aplica una multa del 30%: se retienen {_cancelPenalty:C} de {_paidTotal:C} abonados y se reembolsan {_cancelRefund:C} a tu saldo a favor. Esta acción no puede deshacerse.";
        }

        var confirmed = await DisplayAlertAsync("Cancelar reserva", message, "Cancelar reserva", "Seguir en la reserva");
        if (!confirmed) return;

        CancelButton.IsEnabled = false;
        try
        {
            var result = await _api.CancelBookingAsync(id, reason!);
            var refund = result?.RefundAmount ?? 0;
            var success = refund > 0
                ? $"Reserva cancelada. {(result?.WithinPolicy == true ? "Sin multa." : "Multa 30% aplicada.")} Se abonarán {refund:C} a tu saldo a favor."
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
            return;
        }

        var my = await _api.GetMyTripRatingAsync(trip!.Id);
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
}