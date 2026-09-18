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

            PaymentButton.IsVisible = _booking.Status != BookingStatus.Cancelled;
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
            PaymentButton.IsVisible = _booking.Status != BookingStatus.Cancelled;
            await DisplayAlertAsync("Listo", $"Reserva {status}.", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnRegisterPaymentClicked(object? sender, EventArgs e)
    {
        if (_booking is null) return;

        var methods = Enum.GetNames<PaymentMethod>();
        var selected = await DisplayActionSheetAsync(
            "Método de pago",
            "Cancelar",
            null,
            methods);

        if (string.IsNullOrEmpty(selected) || selected == "Cancelar") return;

        if (!Enum.TryParse<PaymentMethod>(selected, out var method)) return;

        try
        {
            await _api.CreatePaymentAsync(new Payment { BookingId = _booking.Id, Method = method });
            await LoadBookingAsync();
            await DisplayAlertAsync("Pago registrado",
                $"Pago de {_booking.TotalAmount:C} registrado. La reserva quedó confirmada.", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }
}