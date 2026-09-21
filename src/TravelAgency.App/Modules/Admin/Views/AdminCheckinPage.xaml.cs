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
        TripMetaLabel.Text = $"{_trip.Destination} · {_trip.StartDate:dd/MM/yyyy} al {_trip.EndDate:dd/MM/yyyy}";

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
        var passengers = bookings.SelectMany(b => b.Passengers ?? new List<TripPassenger>()).ToList();
        var boarded = bookings.Count(b => b.CheckedIn) + passengers.Count(p => p.CheckedIn);
        var boardedSeats = bookings.Where(b => b.CheckedIn).Sum(b => b.NumberOfSeats) + passengers.Count(p => p.CheckedIn);
        var totalSeats = bookings.Sum(b => b.NumberOfSeats) + passengers.Count;
        ProgressLabel.Text = $"{boarded} de {bookings.Count + passengers.Count} pasajeros abordaron · {boardedSeats} de {totalSeats} asientos";

        if (trip.DepartureCompleted)
        {
            StatusBanner.BackgroundColor = Color.FromArgb("#2F855A");
            StatusLabel.Text = "Salida completada";
            OpenCheckinButton.IsVisible = false;
            CloseCheckinButton.IsVisible = false;
            CompleteButton.IsVisible = false;
            ReopenButton.IsVisible = true;
        }
        else if (trip.CheckInOpen)
        {
            StatusBanner.BackgroundColor = Color.FromArgb("#2B6CB0");
            StatusLabel.Text = "Check-in abierto";
            OpenCheckinButton.IsVisible = false;
            CloseCheckinButton.IsVisible = true;
            CompleteButton.IsVisible = true;
            ReopenButton.IsVisible = false;
        }
        else
        {
            StatusBanner.BackgroundColor = Color.FromArgb("#6B7280");
            StatusLabel.Text = "Check-in cerrado";
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
        var canToggle = !_trip!.DepartureCompleted && booking.Status != BookingStatus.Cancelled && _trip.CheckInOpen;

        var infoText = "Acompañante · 1 asiento";
        if (passenger.CheckedIn && passenger.CheckedInAt.HasValue)
            infoText += $" · Abordó {passenger.CheckedInAt.Value.ToLocalTime():HH:mm}";

        var info = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        info.Children.Add(new Label
        {
            Text = $"{passenger.Name} (de {booking.User?.Name ?? $"#{booking.UserId}"})",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = isCancelled ? Colors.Gray : null
        });
        info.Children.Add(new Label
        {
            Text = infoText,
            FontSize = 13,
            TextColor = Colors.Gray
        });

        var boardedLabel = new Label
        {
            Text = passenger.CheckedIn ? "Abordó" : "No abordó",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(10, 0, 0, 0),
            TextColor = passenger.CheckedIn ? Color.FromArgb("#2F855A") : Colors.Gray
        };

        var switchControl = new Switch
        {
            IsToggled = passenger.CheckedIn,
            IsEnabled = canToggle,
            BindingContext = passenger,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        switchControl.Toggled += OnPassengerBoardToggledAsync;

        var qrButton = new Button
        {
            Text = "QR",
            FontSize = 13,
            Padding = new Thickness(10, 4),
            BackgroundColor = Color.FromArgb("#1976D2"),
            TextColor = Colors.White,
            BindingContext = (booking.Id, passenger.Id),
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        qrButton.Clicked += OnViewPassengerQrClickedAsync;

        var right = new HorizontalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children = { boardedLabel, switchControl, qrButton }
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Padding = new Thickness(12)
        };
        grid.Add(info, 0);
        grid.Add(right, 1);

        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var baseColor = isDark ? Color.FromArgb("#2C2C2C") : Color.FromArgb("#EEEEEE");
        var boardedColor = Color.FromArgb("#E6F4EA");

        return new Border
        {
            StrokeThickness = 0,
            BackgroundColor = passenger.CheckedIn ? boardedColor : baseColor,
            Content = grid,
            Margin = new Thickness(0, 0, 0, 2)
        };
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
        var canToggle = !_trip!.DepartureCompleted && booking.Status != BookingStatus.Cancelled && _trip.CheckInOpen;

        var infoText = $"{booking.NumberOfSeats} asiento(s) · {booking.TotalAmount:C}";
        if (booking.CheckedIn && booking.CheckedInAt.HasValue)
            infoText += $" · Abordó {booking.CheckedInAt.Value.ToLocalTime():HH:mm}";

        var info = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        info.Children.Add(new Label
        {
            Text = name,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = isCancelled ? Colors.Gray : null
        });
        info.Children.Add(new Label
        {
            Text = infoText,
            FontSize = 13,
            TextColor = Colors.Gray
        });

        var boardedLabel = new Label
        {
            Text = booking.Status == BookingStatus.Cancelled ? "Cancelada" : (booking.CheckedIn ? "Abordó" : "No abordó"),
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(10, 0, 0, 0),
            TextColor = booking.CheckedIn ? Color.FromArgb("#2F855A") : Colors.Gray
        };

        var switchControl = new Switch
        {
            IsToggled = booking.CheckedIn,
            IsEnabled = canToggle,
            BindingContext = booking,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        switchControl.Toggled += OnBoardToggledAsync;

        var qrButton = new Button
        {
            Text = "QR",
            FontSize = 13,
            Padding = new Thickness(10, 4),
            BackgroundColor = Color.FromArgb("#1976D2"),
            TextColor = Colors.White,
            BindingContext = booking,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        qrButton.Clicked += OnViewQrClickedAsync;

        var right = new HorizontalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children = { boardedLabel, switchControl, qrButton }
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Padding = new Thickness(12)
        };
        grid.Add(info, 0);
        grid.Add(right, 1);

        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var baseColor = isDark ? Color.FromArgb("#2C2C2C") : Color.FromArgb("#EEEEEE");
        var boardedColor = Color.FromArgb("#E6F4EA");

        return new Border
        {
            StrokeThickness = 0,
            BackgroundColor = booking.CheckedIn ? boardedColor : baseColor,
            Content = grid
        };
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