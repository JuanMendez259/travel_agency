using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(TripId), "tripId")]
[QueryProperty(nameof(Seats), "seats")]
public partial class ClientBookingSeatsPage : ContentPage
{
    private const int ChildMaxAge = 11;

    private static readonly Color AvailableColor = Color.FromArgb("#D9D9D9");
    private static readonly Color SelectedColor = Color.FromArgb("#0A5AAE");
    private static readonly Color OccupiedColor = Color.FromArgb("#DBB92A");

    private readonly ApiService _api;
    private readonly List<SeatSlot> _slots = new();
    private readonly HashSet<int> _taken = new();
    private int _capacity;
    private bool _built;

    public string TripId { get; set; } = string.Empty;
    public string Seats { get; set; } = string.Empty;

    public ClientBookingSeatsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    private sealed class SeatSlot
    {
        public required string Label { get; init; }
        public Entry? NameEntry { get; set; }
        public Entry? AgeEntry { get; set; }
        public int? Seat { get; set; }
        public Label SeatLabel { get; set; } = new();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_built) return;

        if (!int.TryParse(TripId, out var tripId) || !int.TryParse(Seats, out var seats) || seats < 1)
        {
            await DisplayAlertAsync("Error", "No se pudo determinar la cantidad de asientos.", "OK");
            await Shell.Current.GoToAsync("..");
            return;
        }

        await BuildAsync(tripId, seats);
        _built = true;
    }

    private async Task BuildAsync(int tripId, int seats)
    {
        try
        {
            var availability = await _api.GetTripSeatsAsync(tripId);
            if (availability is null)
            {
                await DisplayAlertAsync("Error", "No se encontró el viaje.", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            _capacity = availability.Capacity;
            foreach (var number in availability.OccupiedSeats)
                _taken.Add(number);

            TripLabel.Text = availability.TripTitle;
            SummaryLabel.Text = $"{seats} asiento(s) a elegir · {availability.AvailableCount} libre(s) de {_capacity}";

            if (availability.AvailableCount < seats)
            {
                await DisplayAlertAsync("Sin cupo",
                    $"Solo quedan {availability.AvailableCount} asientos libres y necesitas {seats}.",
                    "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            BuildSlots(seats);
            RenderMap(availability.Rows);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar el mapa de asientos: {ex.Message}", "OK");
            await Shell.Current.GoToAsync("..");
        }
    }

    private void BuildSlots(int seats)
    {
        _slots.Clear();
        PassengersLayout.Clear();

        var holder = new SeatSlot { Label = "Titular (tú)" };
        _slots.Add(holder);
        PassengersLayout.Add(BuildSlotRow(holder, isHolder: true));

        for (var i = 1; i < seats; i++)
        {
            var slot = new SeatSlot { Label = $"Pasajero {i}" };
            _slots.Add(slot);
            PassengersLayout.Add(BuildSlotRow(slot, isHolder: false));
        }
    }

    private View BuildSlotRow(SeatSlot slot, bool isHolder)
    {
        if (!isHolder)
        {
            slot.NameEntry = new Entry { Placeholder = "Nombre", ReturnType = ReturnType.Next };
            slot.AgeEntry = new Entry
            {
                Placeholder = "Edad",
                Keyboard = Keyboard.Numeric,
                WidthRequest = 90
            };

            var nameAge = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8
            };
            nameAge.Add(slot.NameEntry, 0);
            nameAge.Add(slot.AgeEntry, 1);

            var container = new VerticalStackLayout { Spacing = 2 };
            container.Add(new Label { Text = slot.Label, FontSize = 12, TextColor = Colors.DarkGray });
            container.Add(nameAge);
            container.Add(slot.SeatLabel = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold });

            slot.SeatLabel.Text = "Sin asiento";
            return container;
        }

        slot.SeatLabel = new Label { Text = "Sin asiento", FontSize = 13, FontAttributes = FontAttributes.Bold };
        var holderRow = new HorizontalStackLayout { Spacing = 8 };
        holderRow.Add(new Label { Text = slot.Label, FontSize = 13, VerticalOptions = LayoutOptions.Center });
        holderRow.Add(slot.SeatLabel);
        return holderRow;
    }

    private void RenderMap(List<TripSeatRow> rows)
    {
        RowsContainer.Clear();
        foreach (var row in rows)
        {
            var rowLayout = new HorizontalStackLayout
            {
                Spacing = 10,
                HorizontalOptions = LayoutOptions.Center
            };

            foreach (var seat in row.Seats)
            {
                rowLayout.Add(BuildSeat(seat));
            }

            RowsContainer.Add(rowLayout);
        }
    }

    private View BuildSeat(TripSeat seat)
    {
        if (seat.IsAisle)
        {
            return new BoxView
            {
                WidthRequest = 18,
                HeightRequest = 44,
                Color = Colors.Transparent
            };
        }

        var border = new Border
        {
            WidthRequest = 44,
            HeightRequest = 44,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
            Stroke = Colors.Transparent,
            Content = new Label
            {
                Text = seat.Number.ToString(),
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        PaintSeat(border, seat.Number);

        if (!seat.IsOccupied)
        {
            border.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await PickSlotForSeatAsync(seat.Number))
            });        }

        return border;
    }

    private void PaintSeat(Border border, int number)
    {
        var isTaken = _taken.Contains(number);
        var slot = _slots.FirstOrDefault(s => s.Seat == number);
        var label = (Label)border.Content;

        if (isTaken)
        {
            border.BackgroundColor = OccupiedColor;
            label.TextColor = Colors.Black;
            return;
        }

        if (slot is not null)
        {
            border.BackgroundColor = SelectedColor;
            label.TextColor = Colors.White;
            return;
        }

        border.BackgroundColor = AvailableColor;
        label.TextColor = Colors.Black;
    }

    private void RefreshSeatVisuals()
    {
        foreach (var child in RowsContainer.Children)
        {
            if (child is not HorizontalStackLayout row) continue;
            foreach (var seatView in row.Children)
            {
                if (seatView is not Border border || border.Content is not Label label) continue;
                if (!int.TryParse(label.Text, out var number)) continue;
                PaintSeat(border, number);
            }
        }

        foreach (var slot in _slots)
        {
            slot.SeatLabel.Text = slot.Seat.HasValue
                ? $"Asiento {slot.Seat.Value}"
                : "Sin asiento";
            slot.SeatLabel.TextColor = slot.Seat.HasValue ? SelectedColor : Colors.Gray;
        }

        var complete = _slots.All(s => s.Seat.HasValue);
        ConfirmButton.IsEnabled = complete;
        ConfirmButton.Text = complete
            ? $"Confirmar reserva ({_slots.Count} asiento(s))"
            : "Elige los asientos";
    }

    private async Task PickSlotForSeatAsync(int seatNumber)
    {
        var previous = _slots.FirstOrDefault(s => s.Seat == seatNumber);
        if (previous is not null)
        {
            previous.Seat = null;
            RefreshSeatVisuals();
            return;
        }

        var options = _slots
            .Select(s => $"{s.Label}{(s.Seat.HasValue ? $" (mueve del {s.Seat.Value})" : "")}")
            .ToArray();

        var choice = await DisplayActionSheet($"Asiento {seatNumber}", "Cancelar", null, options);
        if (string.IsNullOrEmpty(choice)) return;

        var index = Array.IndexOf(options, choice);
        if (index < 0) return;

        var target = _slots[index];
        target.Seat = seatNumber;
        RefreshSeatVisuals();
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (_slots.Count == 0 || _slots.Any(s => !s.Seat.HasValue)) return;

        var passengers = new List<(string Name, int Age, int SeatNumber)>();
        for (var i = 1; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            var name = slot.NameEntry?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await DisplayAlertAsync("Datos incompletos", $"Completa el nombre del {slot.Label.ToLowerInvariant()}.", "OK");
                return;
            }

            var age = 11;
            if (slot.AgeEntry?.Text is { Length: > 0 } ageText)
            {
                if (!int.TryParse(ageText, out age) || age is < 0 or > 110)
                {
                    await DisplayAlertAsync("Datos incompletos", $"Indica una edad válida para {name}.", "OK");
                    return;
                }
            }

            passengers.Add((name, age, slot.Seat!.Value));
        }

        if (!await PoliciesDisclaimerPage.PresentAsync(Shell.Current.Navigation)) return;

        var holderSeat = _slots[0].Seat!.Value;
        ConfirmButton.IsEnabled = false;
        try
        {
            var created = await _api.CreateBookingWithSeatsAsync(
                int.Parse(TripId), _slots.Count, holderSeat, passengers);

            await DisplayAlertAsync("Reserva creada",
                $"Tu reserva quedó {created?.Status} por un total de {created?.TotalAmount:C}. "
                + $"Asientos: {string.Join(", ", _slots.Select(s => s.Seat!.Value).OrderBy(n => n))}.",
                "OK");

            if (created is not null && created.Id > 0)
                await Shell.Current.GoToAsync($"//mytrips/mybooking?id={created.Id}");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
            RefreshSeatVisuals();
        }
        finally
        {
            RefreshSeatVisuals();
        }
    }
}
