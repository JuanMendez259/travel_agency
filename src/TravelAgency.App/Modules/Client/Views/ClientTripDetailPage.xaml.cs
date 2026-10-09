using System.Globalization;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class ClientTripDetailPage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private Trip? _trip;
    private bool _isFavorite;
    private int _adults = 1;
    private int _children;
    private readonly List<(TripOption Option, int Adults, int Children)> _optionSelections = new();
    private int _availableSeats;
    private bool _bookingClosed;

    public string TripId { get; set; } = string.Empty;

    public ClientTripDetailPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_trip is not null) return;

        if (int.TryParse(TripId, out var tripId))
        {
            try
            {
                await LoadTripAsync(tripId);
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", $"No se pudo cargar el viaje: {ex.Message}", "OK");
            }
        }
    }

    private async Task LoadTripAsync(int tripId)
    {
        _trip = await _api.GetTripAsync(tripId);
        if (_trip is null)
        {
            await DisplayAlertAsync("Error", "No se encontró el viaje.", "OK");
            return;
        }

        try
        {
            var favorites = await _api.GetFavoritesAsync();
            SetFavorite(favorites?.Any(t => t.Id == tripId) == true);
        }
        catch
        {
            SetFavorite(false);
        }

        TitleLabel.Text = _trip.Title;
        DestinationLabel.Text = _trip.Destination;
        DescriptionShortLabel.Text = _trip.Description;
        AdultPriceLabel.Text = _trip.Price.ToString("C");
        var childPrice = _trip.ChildPrice ?? _trip.Price;
        var hasChildFare = _trip.ChildPrice.HasValue;
        ChildPriceLabel.Text = childPrice.ToString("C");
        ChildPriceBadge.IsVisible = hasChildFare;
        ChildPriceHintLabel.Text = hasChildFare ? "De 0 a 11 años" : "De 0 a 11 años · tarifa de adulto";
        ChildrenRow.IsVisible = true;
        DescriptionLabel.Text = _trip.Description;
        CategoryLabel.Text = _trip.Category;
        CategoryLabel.IsVisible = !string.IsNullOrWhiteSpace(_trip.Category);
        TransportLabel.Text = TransportTypeConverter.ToDisplay(_trip.TransportType);
        CapacityTitleLabel.Text = $"Capacidad: {TransportTypeConverter.ToDisplay(_trip.TransportType)}";

        var available = Math.Max(0, _trip.AvailableSeats);
        SeatsLabel.Text = $"{_trip.Capacity - available} confirmados";
        SeatsLeftLabel.Text = $"Quedan {available} de {_trip.Capacity} asientos";
        if (_trip.HasTwoFloors)
        {
            CapacityTitleLabel.Text = $"Capacidad: {TransportTypeConverter.ToDisplay(_trip.TransportType)} · Dos pisos";
            SeatsLeftLabel.Text = $"Quedan {available} de {_trip.Capacity} · Piso 1: {_trip.Floor1Capacity ?? 0} · Piso 2: {_trip.Floor2Capacity ?? 0}";
        }
        UrgentLabel.Text = available > 0 && available <= 3 ? $"⚡ Últimos {available} lugares" : string.Empty;
        UrgentLabel.IsVisible = available > 0 && available <= 3;
        CapacityBar.Progress = _trip.Capacity > 0
            ? Math.Clamp((double)(_trip.Capacity - available) / _trip.Capacity, 0, 1)
            : 0;

        var deadline = _trip.BookingDeadline ?? _trip.StartDate;
        var bookingClosed = DateTime.Today > deadline.Date;
        DeadlineLabel.Text = bookingClosed
            ? $"Reservas cerradas · {deadline:dd/MM/yyyy}"
            : $"Reservas abiertas hasta {deadline:dd/MM/yyyy}";
        DeadlineLabel.IsVisible = true;

        var days = (_trip.EndDate.Date - _trip.StartDate.Date).Days + 1;
        var nights = Math.Max(0, days - 1);
        DurationLabel.Text = $"{days} {(days == 1 ? "Día" : "Días")} / {nights} {(nights == 1 ? "Noche" : "Noches")}";

        ItineraryOutLabel.Text = $"{FormatTripDateTime(_trip.StartDate)}";
        OutMeetingLabel.Text = $"Punto de encuentro: {_trip.Destination}";
        ItineraryBackLabel.Text = $"{FormatTripDateTime(_trip.EndDate)}";
        BackMeetingLabel.Text = $"Llegada al mismo punto de partida ({_trip.Destination}).";

        _availableSeats = available;
        _bookingClosed = bookingClosed;
        BookPanel.IsVisible = available > 0;
        NoCapacityPanel.IsVisible = available <= 0;
        SpecialNeedsSection.IsVisible = available > 0;
        BookClosedLabel.Text = $"Este viaje cerró reservas el {deadline:dd/MM/yyyy}.";
        BookClosedLabel.IsVisible = available > 0 && bookingClosed;
        UpdateBookingSummary();
        RenderOptionsIfAny();

        if (!string.IsNullOrEmpty(_trip.ImageUrl))
            TripImage.Source = await _api.GetTripImageAsync(_trip.ImageUrl);
    }

    private static string FormatTripDateTime(DateTime value)
    {
        var culture = CultureInfo.GetCultureInfo("es-MX");
        var date = value.ToString("ddd d MMM", culture);
        var hour = value.ToString("HH:mm");
        var meridiem = value.Hour < 12 ? "AM" : "PM";
        return $"{char.ToUpper(date[0])}{date[1..]} • {hour} {meridiem}";
    }

    private void UpdateBookingSummary()
    {
        if (_trip?.HasOptions == true && (_trip.Options?.Count ?? 0) > 0)
        {
            var total = 0m;
            var seats = 0;
            foreach (var sel in _optionSelections)
            {
                total += sel.Adults * sel.Option.PriceAdult + sel.Children * (sel.Option.PriceChild ?? sel.Option.PriceAdult);
                seats += sel.Adults + sel.Children;
            }
            AdultsCountLabel.Text = "0";
            ChildrenCountLabel.Text = "0";
            var maxSeatsTotal = Math.Max(0, _availableSeats);
            AdultsPlusButton.IsEnabled = false;
            ChildrenPlusButton.IsEnabled = false;
            AdultsMinusButton.IsEnabled = false;
            ChildrenMinusButton.IsEnabled = false;
            BookButton.IsEnabled = !_bookingClosed && seats >= 1 && seats <= maxSeatsTotal;
            TotalLabel.Text = total.ToString("C");
            SeatsSelectionHintLabel.Text = seats == 1
                ? "(1 asiento seleccionado)"
                : $"({seats} asientos seleccionados)";
            return;
        }

        var adults2 = _adults;
        var children2 = _children;
        var adultPrice2 = _trip?.Price ?? 0;
        var childPrice2 = _trip?.ChildPrice ?? adultPrice2;
        var total2 = adults2 * adultPrice2 + children2 * childPrice2;
        var seats2 = adults2 + children2;

        AdultsCountLabel.Text = adults2.ToString();
        ChildrenCountLabel.Text = children2.ToString();

        var maxSeats2 = Math.Max(0, _availableSeats);
        AdultsPlusButton.IsEnabled = !_bookingClosed && seats2 < maxSeats2;
        ChildrenPlusButton.IsEnabled = !_bookingClosed && seats2 < maxSeats2;
        AdultsMinusButton.IsEnabled = adults2 > 1;
        ChildrenMinusButton.IsEnabled = children2 > 0;
        BookButton.IsEnabled = !_bookingClosed && seats2 >= 1 && seats2 <= maxSeats2;

        TotalLabel.Text = total2.ToString("C");
        SeatsSelectionHintLabel.Text = seats2 == 1
            ? "(1 asiento seleccionado)"
            : $"({seats2} asientos seleccionados)";
    }

    private void OnAdultsMinusClicked(object? sender, EventArgs e)
    {
        if (_adults > 1) _adults--;
        UpdateBookingSummary();
    }

    private void OnAdultsPlusClicked(object? sender, EventArgs e)
    {
        if (_adults + _children < Math.Max(0, _availableSeats)) _adults++;
        UpdateBookingSummary();
    }

    private void OnChildrenMinusClicked(object? sender, EventArgs e)
    {
        if (_children > 0) _children--;
        UpdateBookingSummary();
    }

    private void OnChildrenPlusClicked(object? sender, EventArgs e)
    {
        if (_adults + _children < Math.Max(0, _availableSeats)) _children++;
        UpdateBookingSummary();
    }

    private void SetFavorite(bool isFavorite)
    {
        _isFavorite = isFavorite;
        FavoriteButton.Text = isFavorite ? "♥" : "♡";
        FavoriteButton.TextColor = isFavorite ? Color.FromArgb("#E53E3E") : Colors.Gray;
    }

    private async void OnFavoriteClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        try
        {
            var nowFavorite = _isFavorite
                ? await _api.RemoveFavoriteAsync(_trip.Id)
                : await _api.AddFavoriteAsync(_trip.Id);
            SetFavorite(nowFavorite);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = _trip.Title,
                Text = $"{_trip.Title} · {_trip.Destination}\n" +
                       $"Del {_trip.StartDate:dd/MM/yyyy HH:mm} al {_trip.EndDate:dd/MM/yyyy HH:mm}\n" +
                       $"Precio por persona: {_trip.Price:C}"
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo compartir", ex.Message, "OK");
        }
    }

    private async void OnRequestCapacityClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        if (!int.TryParse(RequestedSeatsEntry.Text, out var seats) || seats < 1)
        {
            await DisplayAlertAsync("Error", "Indica cuántos asientos necesitas.", "OK");
            return;
        }

        RequestCapacityButton.IsEnabled = false;
        try
        {
            await _api.CreateCapacityRequestAsync(_trip.Id, seats, RequestMessageEditor.Text?.Trim());
            await DisplayAlertAsync("Solicitud enviada",
                $"Le avisaremos cuando se habilite más cupo para \"{_trip.Title}\".", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            RequestCapacityButton.IsEnabled = true;
        }
    }

    private void OnSpecialNeedsToggled(object? sender, ToggledEventArgs e)
    {
        SpecialNeedsPanel.IsVisible = e.Value;
    }

    private async void OnBookClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        string? specialNeedsNote = null;
        if (SpecialNeedsSwitch.IsToggled)
        {
            var note = SpecialNeedsEditor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(note))
            {
                await DisplayAlertAsync("Necesidades especiales",
                    "Cuéntanos qué necesitas para tu viaje o desactiva la opción.", "OK");
                return;
            }
            specialNeedsNote = note.Length <= 500 ? note : note[..500];
        }

        if (_trip.HasOptions && (_trip.Options?.Count ?? 0) > 0)
        {
            var seatsOpt = 0;
            foreach (var sel in _optionSelections)
                seatsOpt += sel.Adults + sel.Children;
            if (seatsOpt < 1)
            {
                await DisplayAlertAsync("Error", "Selecciona al menos un asiento.", "OK");
                return;
            }
            if (seatsOpt > Math.Max(0, _availableSeats))
            {
                await DisplayAlertAsync("Error", "No hay suficientes asientos disponibles.", "OK");
                return;
            }
            var store = BookingSelectionStore.Instance;
            store.Clear();
            store.TripId = _trip.Id;
            store.SpecialNeedsNote = specialNeedsNote;
            foreach (var sel in _optionSelections)
            {
                if (sel.Adults > 0 || sel.Children > 0)
                    store.Options.Add(new BookingOptionSelection(sel.Option.Id, sel.Adults, sel.Children, sel.Adults + sel.Children));
            }
            store.TotalSeats = seatsOpt;
            await Shell.Current.GoToAsync($"bookseats?tripId={_trip.Id}&seats={seatsOpt}");
            return;
        }

        var seats = _adults + _children;
        if (seats < 1)
        {
            await DisplayAlertAsync("Error", "Selecciona al menos un asiento.", "OK");
            return;
        }

        if (seats > Math.Max(0, _trip.AvailableSeats))
        {
            await DisplayAlertAsync("Error", "No hay suficientes asientos disponibles.", "OK");
            return;
        }

        var store2 = BookingSelectionStore.Instance;
        store2.Clear();
        store2.TripId = _trip.Id;
        store2.TotalSeats = seats;
        store2.SpecialNeedsNote = specialNeedsNote;
        await Shell.Current.GoToAsync($"bookseats?tripId={_trip.Id}&seats={seats}");
    }

    private void RenderOptionsIfAny()
    {
        OptionsBorder.IsVisible = false;
        if (_trip?.HasOptions != true || _trip.Options is null || _trip.Options.Count == 0)
            return;

        OptionsPanel.Children.Clear();
        _optionSelections.Clear();

        foreach (var opt in _trip.Options.Where(o => o.IsActive).OrderBy(o => o.Order))
        {
            _optionSelections.Add((opt, 0, 0));

            var frame = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Stroke = Color.FromArgb("#E2E7FF"),
                StrokeThickness = 1,
                BackgroundColor = Color.FromArgb("#F8F9FF"),
                Padding = 12,
                Margin = new Thickness(0, 6, 0, 6)
            };

            var title = new Label { Text = opt.Name, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#131B2E") };
            var price = new Label { Text = $"{opt.PriceAdult:C} adulto · {(opt.PriceChild ?? opt.PriceAdult):C} niño", FontSize = 12, TextColor = Color.FromArgb("#404941"), Margin = new Thickness(0,2,0,6) };
            var stack = new VerticalStackLayout { Spacing = 6 };

            stack.Add(title);
            stack.Add(price);

            if (opt.BenefitLines.Count > 0)
            {
                foreach (var b in opt.BenefitLines)
                {
                    stack.Add(new Label { Text = $"• {b}", FontSize = 12, TextColor = Color.FromArgb("#404941") });
                }
            }

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, Margin = new Thickness(0,6,0,0) };

            var adultsLbl = new Label { Text = "Adultos", VerticalOptions = LayoutOptions.Center };
            var adultsMinus = new Button { Text = "−", WidthRequest = 34, HeightRequest = 34, CornerRadius = 17, BackgroundColor = Colors.White, TextColor = Color.FromArgb("#131B2E"), BorderWidth = 1, BorderColor = Color.FromArgb("#E2E7FF") };
            var adultsCount = new Label { Text = "0", WidthRequest = 26, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center };
            var adultsPlus = new Button { Text = "+", WidthRequest = 34, HeightRequest = 34, CornerRadius = 17, BackgroundColor = Color.FromArgb("#006C49"), TextColor = Colors.White };

            var childLbl = new Label { Text = "Niños", VerticalOptions = LayoutOptions.Center };
            var childMinus = new Button { Text = "−", WidthRequest = 34, HeightRequest = 34, CornerRadius = 17, BackgroundColor = Colors.White, TextColor = Color.FromArgb("#131B2E"), BorderWidth = 1, BorderColor = Color.FromArgb("#E2E7FF") };
            var childCount = new Label { Text = "0", WidthRequest = 26, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center };
            var childPlus = new Button { Text = "+", WidthRequest = 34, HeightRequest = 34, CornerRadius = 17, BackgroundColor = Color.FromArgb("#006C49"), TextColor = Colors.White };

            var row = 0;
            grid.Add(adultsLbl, 0, row);
            grid.Add(adultsMinus, 1, row);
            grid.Add(adultsCount, 2, row);
            grid.Add(adultsPlus, 3, row);
            row++;
            grid.Add(childLbl, 0, row);
            grid.Add(childMinus, 1, row);
            grid.Add(childCount, 2, row);
            grid.Add(childPlus, 3, row);

            stack.Add(grid);

            var selIndex = _optionSelections.Count - 1;

            void ApplyDelta(int adultsDelta, int childrenDelta)
            {
                var sel = _optionSelections[selIndex];
                var newAdults = sel.Adults + adultsDelta;
                var newChildren = sel.Children + childrenDelta;
                if (newAdults < 0 || newChildren < 0) return;

                var totalSeatsSel = 0;
                foreach (var x in _optionSelections)
                    totalSeatsSel += x.Adults + x.Children;
                var delta = (newAdults + newChildren) - (sel.Adults + sel.Children);
                if (totalSeatsSel + delta > Math.Max(0, _availableSeats)) return;

                _optionSelections[selIndex] = (sel.Option, newAdults, newChildren);
                adultsCount.Text = newAdults.ToString();
                childCount.Text = newChildren.ToString();
                UpdateBookingSummary();
            }

            adultsMinus.Clicked += (_, __) => ApplyDelta(-1, 0);
            adultsPlus.Clicked += (_, __) => ApplyDelta(1, 0);
            childMinus.Clicked += (_, __) => ApplyDelta(0, -1);
            childPlus.Clicked += (_, __) => ApplyDelta(0, 1);

            frame.Content = stack;
            OptionsPanel.Children.Add(frame);
        }

        OptionsPanel.IsVisible = true;
        OptionsBorder.IsVisible = true;
        AdultsRow.IsVisible = false;
        ChildrenRow.IsVisible = false;
        UpdateBookingSummary();
    }
}