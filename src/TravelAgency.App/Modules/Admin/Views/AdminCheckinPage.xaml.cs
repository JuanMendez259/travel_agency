using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminCheckinPage : ContentPage
{
    private readonly ApiService _api;
    private Trip? _trip;
    private bool _busy;

    public string TripId { get; set; } = string.Empty;

    public AdminCheckinPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadTripAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar el viaje: {ex.Message}", "OK");
        }
    }

    private async Task LoadTripAsync()
    {
        if (!int.TryParse(TripId, out var id)) return;

        var trips = await _api.GetAdminTripsAsync();
        _trip = trips?.FirstOrDefault(t => t.Id == id);
        if (_trip is null)
        {
            await DisplayAlertAsync("Error", "No se encontró el viaje.", "OK");
            return;
        }

        Title = $"Check-in · {_trip.Title}";
        TripTitleLabel.Text = _trip.Title;
        TripMetaLabel.Text = $"📍 {_trip.Destination}  ·  🗓 {_trip.StartDate:dd/MM/yyyy HH:mm} al {_trip.EndDate:dd/MM/yyyy HH:mm}";

        RenderAll();
    }

    private void RenderAll()
    {
        RenderStatus();
        RenderPassengers();
    }

    private void RenderStatus()
    {
        var trip = _trip;
        if (trip is null) return;

        var bookings = trip.Bookings
            .Where(b => b.Status != BookingStatus.Cancelled)
            .ToList();
        var totalPassengers = bookings.Sum(b => 1 + (b.Passengers?.Count ?? 0));
        var totalSeats = bookings.Sum(b => Math.Max(b.NumberOfSeats, 1 + (b.Passengers?.Count ?? 0)));
        var boarded = bookings.Sum(b => b.CheckedIn
            ? 1 + (b.Passengers?.Count ?? 0)
            : b.Passengers?.Count(p => p.CheckedIn) ?? 0);
        var boardedSeats = bookings.Sum(b => b.CheckedIn
            ? Math.Max(b.NumberOfSeats, 1 + (b.Passengers?.Count ?? 0))
            : b.Passengers?.Count(p => p.CheckedIn) ?? 0);

        DepartureTimeLabel.Text = $"Salida {trip.StartDate:HH:mm}";
        ProgressLabel.Text = $"{boarded} de {totalPassengers} pasajeros abordaron · {boardedSeats} de {totalSeats} asientos";
        PassengersStatLabel.Text = $"{boarded}/{totalPassengers}";
        SeatsStatLabel.Text = $"{boardedSeats}/{totalSeats}";
        PendingStatLabel.Text = Math.Max(0, totalPassengers - boarded).ToString();
        StatusProgressBar.Progress = totalSeats > 0 ? (double)boardedSeats / totalSeats : 0;

        if (trip.DepartureCompleted)
        {
            StatusDotLabel.TextColor = Res("SecondaryContainer");
            StatusLabel.Text = "Salida completada";
            ScanButton.IsVisible = false;
            OpenCheckinButton.IsVisible = false;
            CloseCheckinButton.IsVisible = false;
            CompleteButton.IsVisible = false;
            ReopenButton.IsVisible = true;
        }
        else if (trip.CheckInOpen)
        {
            StatusDotLabel.TextColor = Res("SecondaryContainer");
            StatusLabel.Text = "Check-in abierto";
            ScanButton.IsVisible = true;
            OpenCheckinButton.IsVisible = false;
            CloseCheckinButton.IsVisible = true;
            CompleteButton.IsVisible = true;
            ReopenButton.IsVisible = false;
        }
        else
        {
            StatusDotLabel.TextColor = Res("OnPrimaryContainer");
            StatusLabel.Text = "Check-in cerrado";
            ScanButton.IsVisible = true;
            OpenCheckinButton.IsVisible = true;
            CloseCheckinButton.IsVisible = false;
            CompleteButton.IsVisible = true;
            ReopenButton.IsVisible = false;
        }
    }

    private void RenderPassengers()
    {
        var trip = _trip;
        if (trip is null) return;

        PassengersLayout.Children.Clear();
        var bookings = trip.Bookings
            .OrderByDescending(b => b.BookingDate)
            .ToList();

        var activeBookings = bookings.Where(b => b.Status != BookingStatus.Cancelled).ToList();
        var totalPeople = activeBookings.Sum(b => 1 + (b.Passengers?.Count ?? 0));
        var totalSeats = activeBookings.Sum(b => Math.Max(b.NumberOfSeats, 1 + (b.Passengers?.Count ?? 0)));
        var peopleText = totalPeople == 1 ? "1 persona" : $"{totalPeople} personas";
        var seatsText = totalSeats == 1 ? "1 asiento" : $"{totalSeats} asientos";
        PassengersCountLabel.Text = $"{peopleText} · {seatsText}";

        NoPassengersLabel.IsVisible = bookings.Count == 0;
        if (bookings.Count == 0) return;

        foreach (var booking in bookings)
        {
            PassengersLayout.Children.Add(BuildPassengerRow(booking));
            foreach (var passenger in booking.Passengers ?? new List<TripPassenger>())
            {
                PassengersLayout.Children.Add(BuildTripPassengerRow(passenger, booking));
            }
        }
    }

    private View BuildTripPassengerRow(TripPassenger passenger, Booking booking)
    {
        var isCancelled = booking.Status == BookingStatus.Cancelled;
        var canToggle = !_trip!.DepartureCompleted && !isCancelled && _trip.CheckInOpen;

        var name = string.IsNullOrWhiteSpace(passenger.Name) ? "Pasajero" : passenger.Name!;
        var holder = booking.User?.Name ?? booking.User?.Email ?? $"Usuario #{booking.UserId}";
        var displayName = $"{name} (de {holder})";

        var infoText = "Acompañante · 1 asiento";
        if (passenger.CheckedIn && passenger.CheckedInAt.HasValue)
            infoText += $" · Abordó {passenger.CheckedInAt.Value.ToLocalTime():HH:mm}";

        var badgeText = isCancelled ? "Cancelada" : passenger.CheckedIn ? "Abordó" : "No abordó";

        var switchControl = new Switch
        {
            IsToggled = passenger.CheckedIn,
            IsEnabled = canToggle,
            BindingContext = passenger,
            VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(switchControl, $"Abordaje de {name}");
        switchControl.Toggled += OnPassengerBoardToggledAsync;

        Button? qrButton = null;
        if (!string.IsNullOrWhiteSpace(passenger.QrToken))
        {
            qrButton = BuildQrButton();
            SemanticProperties.SetDescription(qrButton, $"Ver QR de {name}");
            qrButton.BindingContext = (booking.Id, passenger.Id);
            qrButton.Clicked += OnViewPassengerQrClickedAsync;
        }

        return BuildPassengerCard(
            AdminDisplay.Initials(name),
            displayName,
            infoText,
            badgeText,
            passenger.CheckedIn,
            isCancelled,
            true,
            "Control individual",
            qrButton,
            switchControl);
    }

    private async void OnPassengerBoardToggledAsync(object? sender, ToggledEventArgs e)
    {
        if (sender is not Switch sw || sw.BindingContext is not TripPassenger passenger || _busy)
            return;

        if (sw.IsToggled == passenger.CheckedIn)
            return;

        _busy = true;
        try
        {
            var updated = await _api.UpdatePassengerCheckinAsync(passenger.Id, sw.IsToggled);
            if (updated is not null)
            {
                passenger.CheckedIn = updated.CheckedIn;
                passenger.CheckedInAt = updated.CheckedInAt;
            }
            RenderAll();
        }
        catch (Exception ex)
        {
            sw.IsToggled = passenger.CheckedIn;
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            _busy = false;
        }
    }

    private async void OnViewPassengerQrClickedAsync(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.BindingContext is not (int bookingId, int passengerId))
            return;
        await Shell.Current.GoToAsync($"bookingqr?bid={bookingId}&pid={passengerId}");
    }

    private View BuildPassengerRow(Booking booking)
    {
        var name = booking.User?.Name ?? booking.User?.Email ?? $"Usuario #{booking.UserId}";
        var isCancelled = booking.Status == BookingStatus.Cancelled;
        var canToggle = !_trip!.DepartureCompleted && !isCancelled && _trip.CheckInOpen;

        var seatsLabel = booking.NumberOfSeats == 1 ? "asiento" : "asientos";
        var infoText = $"{booking.NumberOfSeats} {seatsLabel} · {booking.TotalAmount:C}";
        if (booking.CheckedIn && booking.CheckedInAt.HasValue)
            infoText += $" · Abordó {booking.CheckedInAt.Value.ToLocalTime():HH:mm}";

        var badgeText = isCancelled ? "Cancelada" : booking.CheckedIn ? "Abordó" : "No abordó";

        var switchControl = new Switch
        {
            IsToggled = booking.CheckedIn,
            IsEnabled = canToggle,
            BindingContext = booking,
            VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(switchControl, $"Abordaje de {name}");
        switchControl.Toggled += OnBoardToggledAsync;

        Button? qrButton = null;
        if (!string.IsNullOrWhiteSpace(booking.QrToken))
        {
            qrButton = BuildQrButton();
            SemanticProperties.SetDescription(qrButton, $"Ver QR de {name}");
            qrButton.BindingContext = booking;
            qrButton.Clicked += OnViewQrClickedAsync;
        }

        return BuildPassengerCard(
            AdminDisplay.Initials(booking.User?.Name ?? booking.User?.Email),
            name,
            infoText,
            badgeText,
            booking.CheckedIn,
            isCancelled,
            false,
            "Titular de la reserva",
            qrButton,
            switchControl);
    }

    private static Border BuildPassengerCard(
        string initials,
        string name,
        string infoText,
        string badgeText,
        bool boarded,
        bool cancelled,
        bool companion,
        string note,
        Button? qrButton,
        Switch switchControl)
    {
        var info = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        info.Children.Add(new Label
        {
            Text = name,
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            TextColor = cancelled ? Res("Outline") : Res("OnSurface")
        });
        info.Children.Add(new Label
        {
            Text = infoText,
            FontSize = 11,
            LineBreakMode = LineBreakMode.TailTruncation,
            TextColor = Res("OnSurfaceVariant")
        });

        var top = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        top.Add(BuildAvatar(initials, boarded, companion), 0);
        top.Add(info, 1);
        top.Add(BuildBadge(badgeText, boarded, cancelled), 2);

        var actions = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        if (qrButton is not null)
            actions.Children.Add(qrButton);
        actions.Children.Add(switchControl);

        var bottom = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        bottom.Add(new Label
        {
            Text = note,
            FontSize = 10,
            TextColor = Res("Outline"),
            VerticalOptions = LayoutOptions.Center
        }, 0);
        bottom.Add(actions, 1);

        var content = new VerticalStackLayout { Spacing = 0 };
        content.Children.Add(top);
        content.Children.Add(new BoxView
        {
            HeightRequest = 1,
            BackgroundColor = Res("SurfaceContainerHigh"),
            Margin = new Thickness(0, 10, 0, 10)
        });
        content.Children.Add(bottom);

        return new Border
        {
            Style = StyleResource("AdminCard"),
            Padding = new Thickness(14),
            Margin = new Thickness(companion ? 18 : 0, 0, 0, 0),
            BackgroundColor = cancelled
                ? Res("SurfaceContainerLow")
                : Res("SurfaceContainerLowest"),
            Content = content
        };
    }

    private static View BuildAvatar(string initials, bool boarded, bool companion)
    {
        var size = companion ? 32 : 40;
        return new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = companion ? 11 : 14
            },
            BackgroundColor = boarded ? Res("SecondaryContainer") : Res("SurfaceContainer"),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = initials,
                FontFamily = "OpenSansSemibold",
                FontSize = companion ? 10 : 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = boarded ? Res("OnSuccessContainer") : Res("Primary"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
    }

    private static View BuildBadge(string text, bool boarded, bool cancelled)
    {
        var background = cancelled
            ? Res("ErrorContainer")
            : boarded ? Res("SuccessContainer") : Res("SurfaceContainerLow");
        var foreground = cancelled
            ? Res("OnErrorContainer")
            : boarded ? Res("OnSuccessContainer") : Res("Outline");

        return new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = 100
            },
            BackgroundColor = background,
            Padding = new Thickness(8, 4),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                FontFamily = "OpenSansSemibold",
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = foreground,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
    }

    private static Button BuildQrButton()
    {
        return new Button
        {
            Text = "QR",
            FontFamily = "OpenSansSemibold",
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(10, 2),
            HeightRequest = 44,
            MinimumHeightRequest = 44,
            MinimumWidthRequest = 44,
            CornerRadius = 9,
            BorderWidth = 1,
            BorderColor = Res("OutlineVariant"),
            BackgroundColor = Res("SurfaceContainerLowest"),
            TextColor = Res("Primary"),
            VerticalOptions = LayoutOptions.Center
        };
    }

    private static Color Res(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
            return color;
        return Colors.Transparent;
    }

    private static Style? StyleResource(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Style style)
            return style;
        return null;
    }

    private async void OnBoardToggledAsync(object? sender, ToggledEventArgs e)
    {
        if (sender is not Switch sw || sw.BindingContext is not Booking booking || _busy)
            return;

        if (sw.IsToggled == booking.CheckedIn)
            return;

        _busy = true;
        try
        {
            var updated = await _api.UpdateBookingCheckinAsync(booking.Id, sw.IsToggled);
            if (updated is not null)
            {
                booking.CheckedIn = updated.CheckedIn;
                booking.CheckedInAt = updated.CheckedInAt;
            }
            RenderAll();
        }
        catch (Exception ex)
        {
            sw.IsToggled = booking.CheckedIn;
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task UpdateDepartureAsync(bool? checkInOpen, bool? departureCompleted)
    {
        try
        {
            var updated = await _api.UpdateDepartureAsync(_trip!.Id, checkInOpen, departureCompleted);
            if (updated is not null)
            {
                _trip.CheckInOpen = updated.CheckInOpen;
                _trip.DepartureCompleted = updated.DepartureCompleted;
            }
            RenderAll();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnOpenCheckinClicked(object? sender, EventArgs e)
        => await UpdateDepartureAsync(true, null);

    private async void OnCloseCheckinClicked(object? sender, EventArgs e)
        => await UpdateDepartureAsync(false, null);

    private async void OnCompleteClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Salida completada",
            "¿Marcar esta salida como completada? El check-in quedará cerrado.", "Sí", "No");
        if (confirm)
            await UpdateDepartureAsync(null, true);
    }

    private async void OnReopenClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Reabrir salida",
            "¿Reabrir la salida? Podrás volver a marcar pasajeros.", "Sí", "No");
        if (confirm)
            await UpdateDepartureAsync(null, false);
    }

    private async void OnViewQrClickedAsync(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.BindingContext is not Booking booking)
            return;
        await Shell.Current.GoToAsync($"bookingqr?id={booking.Id}");
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"scancheckin?id={_trip.Id}");
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de tu cuenta?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}
