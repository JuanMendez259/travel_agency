using System.Globalization;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(BookingId), "id")]
public partial class AdminBookingDetailPage : ContentPage
{
    private readonly ApiService _api;
    private Booking? _booking;

    public string BookingId { get; set; } = string.Empty;

    public AdminBookingDetailPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadBookingAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar la reserva: {ex.Message}", "OK");
        }
    }

    private async Task LoadBookingAsync()
    {
        if (int.TryParse(BookingId, out var id))
        {
            _booking = await _api.GetBookingAsync(id);
            if (_booking is null)
            {
                await DisplayAlertAsync("Error", "No se encontró la reserva.", "OK");
                return;
            }

            TripLabel.Text = _booking.Trip?.Title;
            ClientLabel.Text = $"Cliente: {_booking.User?.Name} ({_booking.User?.Email})";
            var discountText = _booking.DiscountAmount > 0
                ? $" · Descuento {_booking.DiscountCode}: -{_booking.DiscountAmount:C}"
                : string.Empty;
            InfoLabel.Text = $"{_booking.BookingDate:dd/MM/yyyy} · {_booking.NumberOfSeats} asientos · Total {_booking.TotalAmount:C}{discountText}";
            StatusLabel.Text = $"Estado: {_booking.Status}";
            var specialNeeds = _booking.SpecialNeedsNote?.Trim();
            SpecialNeedsSection.IsVisible = !string.IsNullOrEmpty(specialNeeds);
            SpecialNeedsLabel.Text = specialNeeds ?? string.Empty;
            PaymentsList.ItemsSource = _booking.Payments;
        if (_booking.HasOptionItems && _booking.Items is not null && _booking.Items.Count > 0)
        {
            ItemsHeader.IsVisible = true;
            ItemsLayout.Children.Clear();
            foreach (var it in _booking.Items)
            {
                var optName = it.TripOption?.Name ?? $"Opción #{it.TripOptionId}";
                var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Padding = new Thickness(0,4,0,4) };
                grid.Add(new Label { Text = $"{optName} · {it.Adults}A/{it.Children}N", FontSize = 12, TextColor = Color.FromArgb("#404941") }, 0, 0);
                grid.Add(new Label { Text = it.LineTotal.ToString("C"), FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#131B2E"), HorizontalOptions = LayoutOptions.End }, 1, 0);
                ItemsLayout.Children.Add(grid);
            }
        }

            RenderPassengersCancelSection();

            var paid = _booking.Payments?.Sum(p => p.Amount) ?? 0;
            var remaining = _booking.TotalAmount - paid;
            BalanceLabel.Text = remaining <= 0m
                ? "Liquidado"
                : $"Pagado {paid:C} de {_booking.TotalAmount:C} · Saldo pendiente {remaining:C}";

            PaymentButton.IsVisible = _booking.Status == BookingStatus.Pending;
            CancelButton.IsVisible = _booking.Status != BookingStatus.Cancelled;
        }
    }

    // Lista, cuando la reserva aun no esta cancelada, una fila por acompanante con
    // la accion de cancelar su boleto individual.
    private void RenderPassengersCancelSection()
    {
        PassengersLayout.Children.Clear();

        var passengers = _booking?.Passengers?.ToList() ?? new List<TripPassenger>();
        var visible = _booking is not null
            && _booking.Status != BookingStatus.Cancelled
            && passengers.Count > 0;
        PassengersHeader.IsVisible = visible;
        if (!visible) return;

        foreach (var passenger in passengers)
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8,
                Padding = new Thickness(12, 8)
            };

            var info = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
            info.Add(new Label
            {
                Text = string.IsNullOrWhiteSpace(passenger.Name) ? "Acompanante" : passenger.Name,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#131B2E")
            });
            var seatText = passenger.SeatNumber is int seat
                ? $"Asiento {seat} · {(passenger.IsChild ? "Nino" : "Adulto")}"
                : (passenger.IsChild ? "Nino" : "Adulto");
            info.Add(new Label { Text = seatText, FontSize = 12, TextColor = Color.FromArgb("#404941") });
            grid.Add(info, 0, 0);

            var cancelButton = new Button
            {
                Text = "Cancelar boleto",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                BackgroundColor = Colors.Transparent,
                TextColor = Color.FromArgb("#BA1A1A"),
                BorderColor = Color.FromArgb("#BA1A1A"),
                BorderWidth = 1,
                CornerRadius = 10,
                Padding = new Thickness(12, 0),
                CommandParameter = passenger,
                VerticalOptions = LayoutOptions.Center
            };
            cancelButton.Clicked += OnCancelTicketClicked;
            grid.Add(cancelButton, 1, 0);

            PassengersLayout.Children.Add(new Border
            {
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb("#EEEEEE"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Content = grid
            });
        }
    }

    private static string BookingItemChoiceLabel(BookingItem item)
    {
        var name = item.OptionName ?? item.TripOption?.Name ?? $"Opcion #{item.TripOptionId}";
        return $"{name} · {item.Adults}A/{item.Children}N · {item.LineTotal:C}";
    }

    private async void OnCancelTicketClicked(object? sender, EventArgs e)
    {
        if (_booking is null) return;
        if (sender is not Button button || button.CommandParameter is not TripPassenger passenger) return;

        var reason = await DisplayPromptAsync(
            "Cancelar boleto",
            $"Indica el motivo para cancelar el boleto de {passenger.Name ?? "este acompanante"}:",
            accept: "Continuar",
            cancel: "Cancelar",
            placeholder: "Describe el motivo");
        if (string.IsNullOrWhiteSpace(reason)) return;

        // Con varias lineas de opciones hay que elegir la entrada del boleto.
        int? bookingItemId = null;
        var items = _booking.Items?.ToList() ?? new List<BookingItem>();
        if (_booking.Trip?.HasOptions == true && items.Count > 1)
        {
            // Numerar cada opcion para que la etiqueta sea unica: el ActionSheet solo
            // devuelve el string pulsado, asi que dos entradas con el mismo texto
            // colisionarian y se elegiria el boleto equivocado.
            var labels = items.Select((it, i) => $"{i + 1}) {BookingItemChoiceLabel(it)}").ToArray();
            var selected = await DisplayActionSheetAsync("Selecciona la entrada", "Cancelar", null, labels);
            if (string.IsNullOrEmpty(selected) || selected == "Cancelar") return;

            var index = Array.IndexOf(labels, selected);
            if (index < 0 || index >= items.Count) return;
            bookingItemId = items[index].Id;
        }

        button.IsEnabled = false;
        try
        {
            var result = await _api.CancelTicketAsync(_booking.Id, passenger.Id, bookingItemId, reason!);
            var refund = result?.RefundAmount ?? 0m;
            var balance = result?.WalletBalance ?? 0m;
            await DisplayAlertAsync(
                "Boleto cancelado",
                $"Se cancelo el boleto de {passenger.Name ?? "el acompanante"}. Reembolso: {refund:C}. Saldo a favor del cliente: {balance:C}.",
                "OK");
            await LoadBookingAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No fue posible cancelar", ex.Message, "OK");
            button.IsEnabled = true;
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cancelar reserva",
            "¿Cancelar esta reserva? Se devolverán los asientos al viaje.", "Sí", "No");
        if (confirm) await ChangeStatusAsync(BookingStatus.Cancelled);
    }

    private async Task ChangeStatusAsync(BookingStatus status)
    {
        try
        {
            _booking = await _api.UpdateBookingStatusAsync(_booking!.Id, status);
            StatusLabel.Text = $"Estado: {_booking!.Status}";
            PaymentButton.IsVisible = _booking.Status == BookingStatus.Pending;
            CancelButton.IsVisible = _booking.Status != BookingStatus.Cancelled;
            await DisplayAlertAsync("Listo", $"Reserva {status}.", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnAuditClicked(object? sender, EventArgs e)
    {
        if (_booking is null) return;
        await Shell.Current.GoToAsync($"auditlog?entity=Booking&id={_booking.Id}");
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnRegisterPaymentClicked(object? sender, EventArgs e)
    {
        if (_booking is null) return;

        var paid = _booking.Payments?.Sum(p => p.Amount) ?? 0;
        var remaining = _booking.TotalAmount - paid;

        if (remaining <= 0m)
        {
            await DisplayAlertAsync("Listo", "La reserva ya está liquidada.", "OK");
            return;
        }

        var methods = Enum.GetNames<PaymentMethod>();
        var selected = await DisplayActionSheetAsync(
            "Método de pago",
            "Cancelar",
            null,
            methods);

        if (string.IsNullOrEmpty(selected) || selected == "Cancelar") return;

        if (!Enum.TryParse<PaymentMethod>(selected, out var method)) return;

        var input = await DisplayPromptAsync(
            "Registrar pago",
            $"Total {_booking.TotalAmount:C}. Saldo pendiente: {remaining:C}. ¿Cuánto recibes?",
            accept: "Registrar",
            cancel: "Cancelar",
            placeholder: remaining.ToString("0.00", CultureInfo.InvariantCulture),
            keyboard: Keyboard.Numeric);

        if (!decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) return;
        if (amount <= 0m) return;

        if (amount > remaining) amount = remaining;
        if (amount <= 0m) return;

        try
        {
            var payment = await _api.CreatePaymentAsync(new Payment
            {
                BookingId = _booking.Id,
                Method = method,
                Amount = amount
            });

            await LoadBookingAsync();

            var newRemaining = _booking!.TotalAmount - ((_booking.Payments?.Sum(p => p.Amount) ?? 0));
            if (newRemaining <= 0m)
            {
                await DisplayAlertAsync("Reserva liquidada",
                    $"Pago de {payment.Amount:C} registrado. La reserva quedó confirmada.", "OK");
            }
            else
            {
                await DisplayAlertAsync("Pago registrado",
                    $"Pago de {payment.Amount:C} registrado. Saldo pendiente: {newRemaining:C}.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }
}