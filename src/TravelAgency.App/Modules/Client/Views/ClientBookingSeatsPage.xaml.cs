using Microsoft.Maui.Controls.Shapes;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(TripId), "tripId")]
[QueryProperty(nameof(Seats), "seats")]
public partial class ClientBookingSeatsPage : ContentPage
{
    private const int ChildMaxAge = 11;
    private const int SeatSize = 44;
    private const int AisleWidth = 30;

    private static readonly Color PrimaryColor = Color.FromArgb("#003B1B");
    private static readonly Color SecondaryColor = Color.FromArgb("#006C49");
    private static readonly Color SurfaceLowestColor = Colors.White;
    private static readonly Color SurfaceLowColor = Color.FromArgb("#F2F3FF");
    private static readonly Color SurfaceContainerColor = Color.FromArgb("#EAEDFF");
    private static readonly Color SurfaceHighColor = Color.FromArgb("#E2E7FF");
    private static readonly Color SurfaceHighestColor = Color.FromArgb("#DAE2FD");
    private static readonly Color SurfaceDimColor = Color.FromArgb("#D2D9F4");
    private static readonly Color OnSurfaceColor = Color.FromArgb("#131B2E");
    private static readonly Color OnSurfaceVariantColor = Color.FromArgb("#404941");
    private static readonly Color OutlineColor = Color.FromArgb("#717970");
    private static readonly Color OutlineVariantColor = Color.FromArgb("#C0C9BE");

    private readonly ApiService _api;
    private readonly SessionService _session;
    private readonly List<SeatSlot> _slots = new();
    private readonly HashSet<int> _taken = new();
    private int _capacity;
    private bool _built;
    private bool _passengersReady;
    private decimal _adultPrice;
    private decimal? _childPrice;
    private decimal _baseTotal;
    private decimal _discountAmount;
    private string? _discountCode;
    private DiscountType? _discountType;
    private decimal? _discountValue;

    public string TripId { get; set; } = string.Empty;
    public string Seats { get; set; } = string.Empty;

    public ClientBookingSeatsPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
    }

    private sealed class SeatSlot
    {
        public required string Label { get; init; }
        public Entry? NameEntry { get; set; }
        public Entry? AgeEntry { get; set; }
        public int? Seat { get; set; }
        public Border SeatBadge { get; set; } = new();
        public Label SeatBadgeIcon { get; set; } = new();
        public Label SeatBadgeText { get; set; } = new();
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
            await LoadFaresAsync(tripId);
            RefreshPassengerGate();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar el mapa de asientos: {ex.Message}", "OK");
            await Shell.Current.GoToAsync("..");
        }
    }

    private async Task LoadFaresAsync(int tripId)
    {
        try
        {
            var trip = await _api.GetTripAsync(tripId);
            if (trip is not null)
            {
                _adultPrice = trip.Price;
                _childPrice = trip.ChildPrice;
            }
        }
        catch
        {
            // sin precios, el total se muestra como no disponible
        }
    }

    private void UpdateSummary()
    {
        var assigned = _slots.Count(s => s.Seat.HasValue);
        var required = _slots.Count;
        AssignedCountLabel.Text = $"{assigned} / {required} asignados";

        var seatList = _slots
            .Select(s => s.Seat.HasValue ? $"#{s.Seat.Value}" : "—")
            .ToArray();
        SelectedSeatsLabel.Text = $"Asientos: {string.Join(", ", seatList)}";

        var missing = required - assigned;

        if (!_passengersReady)
        {
            RemainingLabel.Text = "Completa el nombre y la edad de los pasajeros";
            RemainingLabel.TextColor = OutlineColor;
            ConfirmButton.IsEnabled = false;
            ConfirmButton.Text = "Completa los datos de pasajeros";
        }
        else if (missing > 0)
        {
            RemainingLabel.Text = missing == 1
                ? "Falta asignar 1 asiento"
                : $"Faltan {missing} asientos";
            RemainingLabel.TextColor = OutlineColor;
            ConfirmButton.IsEnabled = false;
            ConfirmButton.Text = missing == 1
                ? "Elige 1 asiento más"
                : $"Elige {missing} asientos más";
        }
        else
        {
            RemainingLabel.Text = "¡Todos los asientos elegidos!";
            RemainingLabel.TextColor = SecondaryColor;
            ConfirmButton.IsEnabled = true;
            ConfirmButton.Text = $"Confirmar reserva ({required} asientos)";
        }

        UpdateTotal();
    }

    private void UpdateTotal()
    {
        if (_adultPrice <= 0)
        {
            TotalLabel.Text = "No disponible";
            FareBreakdownLabel.Text = "No pudimos consultar las tarifas del viaje.";
            return;
        }

        var childFare = _childPrice ?? _adultPrice;
        var adults = 1;
        var children = 0;

        for (var i = 1; i < _slots.Count; i++)
        {
            var age = ReadAge(_slots[i]);
            if (age <= ChildMaxAge) children++;
            else adults++;
        }

        _baseTotal = adults * _adultPrice + children * childFare;

        var parts = new List<string> { $"{adults} adulto(s) × {_adultPrice.ToString("C")}" };
        if (children > 0)
        {
            parts.Add(_childPrice.HasValue
                ? $"{children} niño(s) × {childFare.ToString("C")}"
                : $"{children} niño(s) × {_adultPrice.ToString("C")} (tarifa de adulto)");
        }

        var total = _baseTotal;

        if (!string.IsNullOrEmpty(_discountCode) && _discountType.HasValue && _discountValue.HasValue)
        {
            var temp = new Discount { Type = _discountType.Value, Value = _discountValue.Value };
            _discountAmount = temp.ComputeDiscount(_baseTotal);
            total = Math.Max(0, _baseTotal - _discountAmount);
            parts.Add($"Descuento {_discountCode}: −{_discountAmount.ToString("C")}");

            DiscountStatusLabel.IsVisible = true;
            DiscountStatusLabel.Text = $"Descuento aplicado: −{_discountAmount.ToString("C")}";
        }
        else
        {
            _discountAmount = 0;
        }

        TotalLabel.Text = total.ToString("C");
        FareBreakdownLabel.Text = string.Join("  ·  ", parts);
    }

    private static int ReadAge(SeatSlot slot)
    {
        if (slot.AgeEntry?.Text is { Length: > 0 } text && int.TryParse(text, out var age))
            return Math.Clamp(age, 0, 110);

        return ChildMaxAge;
    }

    private bool AllPassengersComplete()
    {
        for (var i = 1; i < _slots.Count; i++)
        {
            var name = _slots[i].NameEntry?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return false;

            var ageText = _slots[i].AgeEntry?.Text?.Trim();
            if (string.IsNullOrEmpty(ageText) ||
                !int.TryParse(ageText, out var age) || age is < 0 or > 110)
                return false;
        }

        return true;
    }

    private void RefreshPassengerGate()
    {
        _passengersReady = AllPassengersComplete();

        SeatGateHintLabel.Text = _passengersReady
            ? "Ya puedes tocar un asiento libre para asignarlo a un viajero."
            : "Completa el nombre y la edad de cada pasajero para poder elegir asientos.";
        SeatGateHintLabel.TextColor = _passengersReady ? SecondaryColor : OnSurfaceVariantColor;

        RefreshSeatVisuals();
    }

    private string SlotDisplayName(int index)
    {
        if (index == 0)
        {
            var holder = _session.UserName?.Trim();
            return string.IsNullOrWhiteSpace(holder) ? "Titular (tú)" : holder;
        }

        var name = _slots[index].NameEntry?.Text?.Trim();
        return string.IsNullOrWhiteSpace(name) ? _slots[index].Label : name;
    }

    private void BuildSlots(int seats)
    {
        _slots.Clear();
        PassengersLayout.Clear();

        for (var i = 0; i < seats; i++)
        {
            var slot = new SeatSlot { Label = i == 0 ? "Titular (tú)" : $"Pasajero {i + 1}" };
            _slots.Add(slot);
            PassengersLayout.Add(BuildSlotCard(slot, i, isHolder: i == 0));
        }
    }

    private View BuildSlotCard(SeatSlot slot, int index, bool isHolder)
    {
        var numberCircle = new Border
        {
            WidthRequest = 28,
            HeightRequest = 28,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            BackgroundColor = isHolder ? PrimaryColor : SurfaceHighColor,
            Content = new Label
            {
                Text = (index + 1).ToString(),
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = isHolder ? Colors.White : OnSurfaceColor,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var titleBlock = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };
        titleBlock.Add(new Label
        {
            Text = slot.Label,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = OnSurfaceColor
        });
        if (isHolder)
        {
            titleBlock.Add(new Label
            {
                Text = "Adulto responsable de reserva",
                FontSize = 11,
                TextColor = OnSurfaceVariantColor
            });
        }

        var identity = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        identity.Add(numberCircle);
        identity.Add(titleBlock);

        BuildSeatBadge(slot);

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        header.Add(identity, 0, 0);
        var badgeHolder = new HorizontalStackLayout
        {
            Padding = new Thickness(10, 4),
            BackgroundColor = Colors.Transparent,
            Children = { slot.SeatBadge }
        };
        header.Add(badgeHolder, 1, 0);

        var card = new VerticalStackLayout { Spacing = 8 };
        card.Add(header);

        if (isHolder)
        {
            return WrapCard(card);
        }

        slot.NameEntry = new Entry
        {
            Placeholder = "Nombre y apellido",
            ReturnType = ReturnType.Next,
            BackgroundColor = Colors.Transparent
        };
        slot.AgeEntry = new Entry
        {
            Placeholder = "Edad",
            Keyboard = Keyboard.Numeric,
            BackgroundColor = Colors.Transparent,
            HorizontalTextAlignment = TextAlignment.Center
        };
        slot.NameEntry.TextChanged += (_, _) => RefreshPassengerGate();
        slot.AgeEntry.TextChanged += (_, _) => RefreshPassengerGate();

        var inputs = new Grid { ColumnSpacing = 8 };
        inputs.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        inputs.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(76, GridUnitType.Absolute)));
        inputs.Add(WrapInput(slot.NameEntry), 0, 0);
        inputs.Add(WrapInput(slot.AgeEntry), 1, 0);

        card.Add(inputs);
        return WrapCard(card);
    }

    private static View WrapCard(View content)
    {
        return new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = SurfaceLowColor,
            Padding = new Thickness(12),
            Content = content
        };
    }

    private static View WrapInput(Entry entry)
    {
        return new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            BackgroundColor = SurfaceLowestColor,
            Padding = new Thickness(10, 0),
            Content = entry
        };
    }

    private static void BuildSeatBadge(SeatSlot slot)
    {
        slot.SeatBadgeIcon = new Label { FontSize = 12, VerticalOptions = LayoutOptions.Center };
        slot.SeatBadgeText = new Label
        {
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };

        var badgeLayout = new HorizontalStackLayout { Spacing = 4 };
        badgeLayout.Add(slot.SeatBadgeIcon);
        badgeLayout.Add(slot.SeatBadgeText);

        slot.SeatBadge = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(11) },
            Padding = new Thickness(10, 4),
            Content = badgeLayout
        };
    }

    private static void PaintSeatBadge(SeatSlot slot)
    {
        if (slot.Seat.HasValue)
        {
            slot.SeatBadge.BackgroundColor = SecondaryColor;
            slot.SeatBadgeIcon.Text = "💺";
            slot.SeatBadgeIcon.TextColor = Colors.White;
            slot.SeatBadgeText.Text = $"Asiento {slot.Seat.Value}";
            slot.SeatBadgeText.TextColor = Colors.White;
        }
        else
        {
            slot.SeatBadge.BackgroundColor = SurfaceHighestColor;
            slot.SeatBadgeIcon.Text = "⏳";
            slot.SeatBadgeIcon.TextColor = OutlineColor;
            slot.SeatBadgeText.Text = "Sin asignar";
            slot.SeatBadgeText.TextColor = OutlineColor;
        }
    }

    private void RenderMap(List<TripSeatRow> rows)
    {
        RowsContainer.Clear();
        foreach (var row in rows)
        {
            var rowLayout = new Grid { ColumnSpacing = 4 };
            rowLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            rowLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(AisleWidth, GridUnitType.Absolute)));
            rowLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var left = new HorizontalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center
            };
            var right = new HorizontalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center
            };

            var seatCount = 0;
            foreach (var seat in row.Seats)
            {
                if (seat.IsAisle) continue;
                var target = seatCount < 2 ? left : right;
                target.Add(BuildSeat(seat));
                seatCount++;
            }

            var aisle = new Label
            {
                Text = $"F{row.RowNumber}",
                FontSize = 10,
                TextColor = OutlineVariantColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };

            rowLayout.Add(left, 0, 0);
            rowLayout.Add(aisle, 1, 0);
            rowLayout.Add(right, 2, 0);
            RowsContainer.Add(rowLayout);
        }
    }

    private View BuildSeat(TripSeat seat)
    {
        var numberLabel = new Label
        {
            Text = seat.Number.ToString(),
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = OnSurfaceColor,
            HorizontalTextAlignment = TextAlignment.Center
        };

        var statusLabel = new Label
        {
            Text = "libre",
            FontSize = 8,
            TextColor = OnSurfaceVariantColor,
            HorizontalTextAlignment = TextAlignment.Center
        };

        var content = new VerticalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        content.Add(numberLabel);
        content.Add(statusLabel);

        var border = new Border
        {
            WidthRequest = SeatSize,
            HeightRequest = SeatSize,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            ClassId = seat.Number.ToString(),
            Content = content
        };

        PaintSeat(border, seat.Number);

        if (!seat.IsOccupied)
        {
            border.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await PickSlotForSeatAsync(seat.Number))
            });
        }

        return border;
    }

    private void PaintSeat(Border border, int number)
    {
        var isTaken = _taken.Contains(number);
        var isSelected = !isTaken && _slots.Any(s => s.Seat == number);

        if (border.Content is not VerticalStackLayout content || content.Children.Count < 2)
            return;

        var numberLabel = (Label)content.Children[0];
        var statusLabel = (Label)content.Children[1];

        if (isTaken)
        {
            border.BackgroundColor = SurfaceDimColor;
            border.Opacity = 0.75;
            border.Scale = 1;
            numberLabel.TextColor = OutlineColor;
            statusLabel.Text = "✕";
            statusLabel.FontSize = 10;
            statusLabel.TextColor = OutlineColor;
            return;
        }

        if (isSelected)
        {
            border.BackgroundColor = SecondaryColor;
            border.Opacity = 1;
            border.Scale = 1.05;
            numberLabel.TextColor = Colors.White;
            numberLabel.FontSize = 10;
            statusLabel.Text = "✓";
            statusLabel.FontSize = 11;
            statusLabel.FontAttributes = FontAttributes.Bold;
            statusLabel.TextColor = Colors.White;
            return;
        }

        if (!_passengersReady)
        {
            border.BackgroundColor = SurfaceContainerColor;
            border.Opacity = 1;
            border.Scale = 1;
            numberLabel.TextColor = OutlineColor;
            numberLabel.FontSize = 10;
            numberLabel.FontAttributes = FontAttributes.Bold;
            statusLabel.Text = "🔒";
            statusLabel.FontSize = 10;
            statusLabel.FontAttributes = FontAttributes.None;
            statusLabel.TextColor = OutlineColor;
            return;
        }

        border.BackgroundColor = SurfaceHighestColor;
        border.Opacity = 1;
        border.Scale = 1;
        numberLabel.TextColor = OnSurfaceColor;
        numberLabel.FontSize = 11;
        numberLabel.FontAttributes = FontAttributes.Bold;
        statusLabel.Text = "libre";
        statusLabel.FontSize = 8;
        statusLabel.FontAttributes = FontAttributes.None;
        statusLabel.TextColor = OnSurfaceVariantColor;
    }

    private void RefreshSeatVisuals()
    {
        foreach (var child in RowsContainer.Children)
        {
            if (child is not Grid grid) continue;
            foreach (var section in grid.Children)
            {
                if (section is not HorizontalStackLayout seats) continue;
                foreach (var seatView in seats.Children)
                {
                    if (seatView is not Border border) continue;
                    if (!int.TryParse(border.ClassId, out var number)) continue;
                    PaintSeat(border, number);
                }
            }
        }

        foreach (var slot in _slots)
            PaintSeatBadge(slot);

        UpdateSummary();
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

        if (!_passengersReady)
        {
            await DisplayAlertAsync("Completa los datos",
                "Primero llena el nombre y la edad de cada pasajero para poder elegir asientos.", "OK");
            return;
        }

        // Nombres reales de cada viajero; se desambiguan si se repiten.
        var names = _slots.Select((_, i) => SlotDisplayName(i)).ToList();
        for (var i = 0; i < names.Count; i++)
        {
            if (names.Where((n, j) => j != i && n == names[i]).Any())
                names[i] = $"{names[i]} ({_slots[i].Label})";
        }

        var options = _slots
            .Select((s, i) => s.Seat.HasValue ? $"{names[i]} (mueve del {s.Seat.Value})" : names[i])
            .ToArray();

        var choice = await DisplayActionSheet($"Asiento {seatNumber}", "Cancelar", null, options);
        if (string.IsNullOrEmpty(choice)) return;

        var index = Array.IndexOf(options, choice);
        if (index < 0) return;

        var target = _slots[index];
        target.Seat = seatNumber;
        RefreshSeatVisuals();
    }

    private async void OnApplyDiscountClicked(object? sender, EventArgs e)
    {
        var code = DiscountCodeEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            ClearDiscount();
            await DisplayAlertAsync("Código", "Escribe un código de descuento.", "OK");
            return;
        }

        if (!int.TryParse(TripId, out var tripId)) return;

        ApplyDiscountButton.IsEnabled = false;
        try
        {
            var result = await _api.ValidateDiscountAsync(code, tripId, _baseTotal);
            if (result is null || !result.Valid)
            {
                ClearDiscount();
                DiscountStatusLabel.IsVisible = true;
                DiscountStatusLabel.TextColor = Color.FromArgb("#BA1A1A");
                DiscountStatusLabel.Text = result?.Message ?? "No se pudo validar el código.";
                return;
            }

            _discountCode = result.Code ?? code.ToUpperInvariant();
            _discountType = result.Type;
            _discountValue = result.Value;
            DiscountCodeEntry.Text = _discountCode;
            DiscountStatusLabel.IsVisible = true;
            DiscountStatusLabel.TextColor = SecondaryColor;
            DiscountStatusLabel.Text = $"Descuento aplicado: −{result.DiscountAmount.ToString("C")}";
            UpdateTotal();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            ApplyDiscountButton.IsEnabled = true;
        }
    }

    private void ClearDiscount()
    {
        _discountCode = null;
        _discountType = null;
        _discountValue = null;
        _discountAmount = 0;
        DiscountStatusLabel.IsVisible = false;
        UpdateTotal();
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (_slots.Count == 0)
        {
            await DisplayAlertAsync("Error", "No hay asientos que reservar.", "OK");
            return;
        }

        if (!_passengersReady)
        {
            await DisplayAlertAsync("Completa los datos",
                "Completa el nombre y la edad de cada pasajero antes de confirmar.", "OK");
            return;
        }

        var missing = _slots.Count(s => !s.Seat.HasValue);
        if (missing > 0)
        {
            await DisplayAlertAsync("Faltan asientos",
                $"Selecciona un asiento para las {missing} persona(s) restante(s) antes de confirmar.", "OK");
            return;
        }

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
            var storeSel = BookingSelectionStore.Instance;
            var sameTrip = storeSel.TripId == int.Parse(TripId);
            List<BookingOptionInput>? optList = null;
            if (sameTrip && storeSel.Options.Count > 0)
            {
                optList = storeSel.Options
                    .Select(o => new BookingOptionInput(o.TripOptionId, o.Adults, o.Children))
                    .Where(o => o.Adults > 0 || o.Children > 0)
                    .ToList();
            }
            var created = await _api.CreateBookingWithSeatsAsync(
                int.Parse(TripId), _slots.Count, holderSeat, passengers, optList, _discountCode,
                sameTrip ? storeSel.SpecialNeedsNote : null);

            if (created is not null && created.Id > 0)
            {
                // Limpia la pestaña Explorar (home -> viaje -> asientos) y abre el detalle de la reserva.
                await Shell.Current.GoToAsync("//home", animate: false);
                await Shell.Current.GoToAsync($"//mytrips/mybooking?id={created.Id}");
            }
            else
            {
                await DisplayAlertAsync("Reserva creada",
                    "Tu reserva se registró, pero no se pudo abrir el detalle.", "OK");
            }
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