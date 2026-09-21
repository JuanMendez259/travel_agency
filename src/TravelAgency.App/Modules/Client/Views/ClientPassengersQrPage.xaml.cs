using Microsoft.Maui.Controls;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(BookingId), "id")]
public partial class ClientPassengersQrPage : ContentPage
{
    private readonly ApiService _api;

    public string BookingId { get; set; } = string.Empty;

    public ClientPassengersQrPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar los pasajeros: {ex.Message}", "OK");
        }
    }

    private async Task LoadAsync()
    {
        if (!int.TryParse(BookingId, out var id)) return;

        var booking = await _api.GetBookingAsync(id);
        if (booking is null)
        {
            await DisplayAlertAsync("Error", "No se encontró la reserva.", "OK");
            return;
        }

        Title = booking.Trip?.Title ?? "QR de Acompañantes";
        TripLabel.Text = $"{booking.Trip?.Title} · {booking.NumberOfSeats} asiento(s)";

        var passengers = booking.Passengers?.ToList()
            ?? new List<TripPassenger>();

        NoPassengersLabel.IsVisible = passengers.Count == 0;
        PassengersLayout.Children.Clear();

        foreach (var passenger in passengers)
        {
            var isCancelled = booking.Status == BookingStatus.Cancelled;
            var stateColor = passenger.CheckedIn ? Color.FromArgb("#2F855A") : Colors.Gray;
            var stateText = passenger.CheckedIn
                ? (passenger.CheckedInAt.HasValue ? $"Abordó {passenger.CheckedInAt.Value.ToLocalTime():HH:mm}" : "Abordó")
                : "No ha abordado";

            var qr = QrCodeService.FromToken(passenger.QrToken);
            var card = new Border
            {
                StrokeThickness = 0,
                BackgroundColor = isCancelled ? null : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#2C2C2C") : Color.FromArgb("#EEEEEE")),
                Padding = new Thickness(16)
            };

            var layout = new VerticalStackLayout { Spacing = 8 };
            layout.Children.Add(new Label
            {
                Text = passenger.Name,
                FontSize = 17,
                FontAttributes = FontAttributes.Bold,
                TextColor = isCancelled ? Colors.Gray : null,
                HorizontalOptions = LayoutOptions.Center
            });

            if (qr is not null)
            {
                layout.Children.Add(new Image
                {
                    Source = qr,
                    HeightRequest = 220,
                    WidthRequest = 220,
                    HorizontalOptions = LayoutOptions.Center
                });
                layout.Children.Add(new Label
                {
                    Text = stateText,
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = stateColor,
                    HorizontalOptions = LayoutOptions.Center
                });
            }
            else
            {
                layout.Children.Add(new Label
                {
                    Text = "Sin código QR",
                    TextColor = Colors.Gray,
                    HorizontalOptions = LayoutOptions.Center
                });
            }

            card.Content = layout;
            PassengersLayout.Children.Add(card);
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}