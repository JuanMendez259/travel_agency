using Microsoft.Maui.Controls;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(BookingId), "bookingId")]
[QueryProperty(nameof(Count), "count")]
[QueryProperty(nameof(TripId), "tripId")]
[QueryProperty(nameof(Seats), "seats")]
public partial class AddPassengersPage : ContentPage
{
    private const int ChildMaxAge = 11;

    private readonly ApiService _api;
    private readonly List<(Entry Name, Entry Age, Label Tag)> _rows = new();
    private int _bookingId;
    private int _count;
    private int _tripId;
    private int _seats;
    private bool _built;

    public string BookingId { get; set; } = string.Empty;
    public string Count { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string Seats { get; set; } = string.Empty;

    public AddPassengersPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_built) return;

        _bookingId = int.TryParse(BookingId, out var bid) ? bid : 0;
        _count = int.TryParse(Count, out var c) ? c : 0;
        _tripId = int.TryParse(TripId, out var tid) ? tid : 0;
        _seats = int.TryParse(Seats, out var s) ? s : 0;

        var isCreate = _tripId > 0 && _seats > 1;

        if (isCreate)
        {
            Title = "Acompañantes de tu reserva";
            IntroLabel.Text = "Registra nombre y edad de cada acompañante. El asiento principal es tuyo y se cobra como adulto.";
            ConfirmButton.Text = "Confirmar reserva";
            BackButton.Text = "Volver";
            _count = _seats - 1;

            try
            {
                var trip = await _api.GetTripAsync(_tripId);
                if (trip is not null)
                    FareHintLabel.Text = "Adulto: " + trip.Price.ToString("C")
                        + (trip.ChildPrice.HasValue ? $" · Niño (0 a {ChildMaxAge} años): {trip.ChildPrice.Value:C}" : "");
            }
            catch
            {
                // sin detalle de precios, continuar
            }
        }
        else
        {
            Title = _count == 1 ? "Pasajero adicional" : "Pasajeros adicionales";
            IntroLabel.Text = "Registra a las personas que viajarán contigo para generar su código QR individual.";
            ConfirmButton.Text = "Guardar pasajeros";
            BackButton.Text = "Volver a mi reserva";

            try
            {
                var booking = await _api.GetBookingAsync(_bookingId);
                if (booking?.Trip is not null)
                    FareHintLabel.Text = "Adulto: " + booking.Trip.Price.ToString("C")
                        + (booking.Trip.ChildPrice.HasValue ? $" · Niño (0 a {ChildMaxAge} años): {booking.Trip.ChildPrice.Value:C}" : "");
            }
            catch
            {
                // sin detalle de precios, continuar
            }
        }

        ConfirmButton.IsEnabled = _count > 0;

        if (_count <= 0)
        {
            FareHintLabel.IsVisible = false;
            EntriesLayout.Children.Add(new Label
            {
                Text = "No hay asientos adicionales que registrar en este momento.",
                FontSize = 14,
                TextColor = Colors.Gray
            });
            _built = true;
            return;
        }

        for (var i = 0; i < _count; i++)
        {
            EntriesLayout.Children.Add(BuildPassengerRow(i));
        }

        _built = true;
    }

    private View BuildPassengerRow(int index)
    {
        var nameEntry = new Entry
        {
            Placeholder = $"Nombre del pasajero {index + 1}",
            ReturnType = ReturnType.Next
        };

        var ageEntry = new Entry
        {
            Placeholder = "Edad",
            Keyboard = Keyboard.Numeric,
            WidthRequest = 110,
            ReturnType = ReturnType.Done
        };

        var tag = new Label
        {
            FontSize = 13,
            TextColor = Colors.Gray,
            Margin = new Thickness(0, 4, 0, 0)
        };

        ageEntry.TextChanged += (_, _) => UpdateTag(tag, ageEntry);

        var nameAgeRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        nameAgeRow.Add(nameEntry, 0);
        nameAgeRow.Add(ageEntry, 1);

        var container = new VerticalStackLayout
        {
            Spacing = 0
        };
        container.Add(new Label { Text = $"Pasajero {index + 1}", FontSize = 13, TextColor = Colors.DarkGray });
        container.Add(nameAgeRow);
        container.Add(tag);

        _rows.Add((nameEntry, ageEntry, tag));
        return container;
    }

    private static void UpdateTag(Label tag, Entry ageEntry)
    {
        if (int.TryParse(ageEntry.Text, out var age) && age is >= 0 and <= 110)
        {
            tag.Text = age <= ChildMaxAge ? "Niño" : "Adulto";
        }
        else
        {
            tag.Text = string.Empty;
        }
    }

    private static bool IsValidAge(int? age) => age is >= 0 and <= 110;

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (_count <= 0) return;

        var passengers = new List<(string Name, int Age)>();
        foreach (var (nameEntry, ageEntry, _) in _rows)
        {
            var name = nameEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await DisplayAlertAsync("Datos incompletos", "Completa el nombre de todos los pasajeros.", "OK");
                return;
            }

            if (!int.TryParse(ageEntry.Text, out var age) || !IsValidAge(age))
            {
                await DisplayAlertAsync("Datos incompletos", $"Indica una edad válida para {name}.", "OK");
                return;
            }

            passengers.Add((name, age));
        }

        ConfirmButton.IsEnabled = false;
        try
        {
            if (_tripId > 0 && _seats > 1)
            {
                var created = await _api.CreateBookingAsync(_tripId, _seats, passengers);
                await DisplayAlertAsync("Reserva creada",
                    $"Tu reserva quedó {created?.Status} por un total de {created?.TotalAmount:C}. Ya puedes ver tus códigos QR.",
                    "OK");
                if (created is not null && created.Id > 0)
                    await Shell.Current.GoToAsync($"mybooking?id={created.Id}");
            }
            else
            {
                await _api.CreatePassengersAsync(_bookingId, passengers);
                await DisplayAlertAsync("Listo", "Acompañantes registrados. Ya puedes ver sus códigos QR.", "OK");
                await Shell.Current.GoToAsync("..");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            ConfirmButton.IsEnabled = true;
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}