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
            InfoLabel.Text = $"{_booking.BookingDate:dd/MM/yyyy} · {_booking.NumberOfSeats} asientos · Total {_booking.TotalAmount:C}";
            StatusLabel.Text = $"Estado: {_booking.Status}";
            PaymentsList.ItemsSource = _booking.Payments;

            var paid = _booking.Payments?.Sum(p => p.Amount) ?? 0;
            var remaining = _booking.TotalAmount - paid;
            BalanceLabel.Text = remaining <= 0m
                ? "Liquidado"
                : $"Pagado {paid:C} de {_booking.TotalAmount:C} · Saldo pendiente {remaining:C}";

            PaymentButton.IsVisible = _booking.Status == BookingStatus.Pending;
            CancelButton.IsVisible = _booking.Status != BookingStatus.Cancelled;
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