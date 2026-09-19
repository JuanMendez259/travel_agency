using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Controls;
using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(BookingId), "id")]
public partial class ClientMyBookingDetailPage : ContentPage
{
    private static readonly JsonSerializerOptions MapJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ApiService _api;

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

        var trip = booking.Trip;

        Title = trip?.Title;
        TitleLabel.Text = trip?.Title;
        DestinationLabel.Text = trip?.Destination;
        DatesLabel.Text = trip is null
            ? ""
            : $"{trip.StartDate:dd/MM/yyyy} al {trip.EndDate:dd/MM/yyyy}";
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

        var paid = booking.Payments?.Sum(p => p.Amount) ?? 0;
        var total = booking.TotalAmount;
        var remaining = total - paid;

        BalanceLabel.Text = booking.Status == BookingStatus.Cancelled
            ? $"Pagado {paid:C} · Reserva cancelada"
            : paid >= total
                ? "Liquidado · ¡Reserva confirmada!"
                : $"Pagado {paid:C} de {total:C} · Saldo pendiente {remaining:C}";

        PaymentsList.ItemsSource = booking.Payments;

        PaymentsTotalLabel.Text = booking.Payments is { Count: > 0 }
            ? $"Total abonado: {paid:C}"
            : "";
        PaymentsTotalLabel.IsVisible = booking.Payments is { Count: > 0 };

        if (!string.IsNullOrEmpty(trip?.ImageUrl))
            TripImage.Source = await _api.GetTripImageAsync(trip.ImageUrl);
    }

    private async Task LoadTripMapAsync(Trip? trip)
    {
        var hasAnyRoute = trip is not null &&
            (trip.OriginLatitude.HasValue || trip.DestinationLatitude.HasValue ||
             trip.PointsOfInterest is { Count: > 0 });

        if (!hasAnyRoute || string.IsNullOrEmpty(MapConfig.GoogleMapsApiKey))
            return;

        var cfg = new
        {
            readOnly = true,
            originLat = trip!.OriginLatitude,
            originLng = trip.OriginLongitude,
            destLat = trip.DestinationLatitude,
            destLng = trip.DestinationLongitude,
            pois = trip.PointsOfInterest
                .OrderBy(p => p.Order)
                .Select(p => new { p.Id, p.Name, p.Description, lat = p.Latitude, lng = p.Longitude })
        };

        var cfgJson = JsonSerializer.Serialize(cfg, MapJsonOptions);
        var html = await ReadMapHtmlAsync();
        html = html
            .Replace("{{KEY}}", MapConfig.GoogleMapsApiKey)
            .Replace("{{CFG}}", "const CFG = " + cfgJson + ";");

        TripMapView.Source = new HtmlWebViewSource { Html = html };
        MapHeader.IsVisible = true;
        TripMapView.IsVisible = true;
    }

    private static async Task<string> ReadMapHtmlAsync()
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync("map.html");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}