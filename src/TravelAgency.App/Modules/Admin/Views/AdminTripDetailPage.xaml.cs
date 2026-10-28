using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Shapes;
using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminTripDetailPage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private Trip? _trip;
    private TripTransportInfo? _transportInfo;
    private FileResult? _selectedImage;

    public string TripId { get; set; } = string.Empty;

    private static Color Resource(string key) => (Color)Application.Current!.Resources[key];

    public AdminTripDetailPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;

        if (_session.IsCoordinator)
        {
            PaymentsButton.IsVisible = false;
            MapButton.IsVisible = false;
            AuditButton.IsVisible = false;
            EditActions.IsVisible = false;
            ManageOptionsButton.IsVisible = false;
        }

        AddTransportUnitButton.IsVisible = _session.IsAdmin;
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

        Title = _trip.Title;
        TitleLabel.Text = _trip.Title;
        DestinationLabel.Text = _trip.Destination;
        DatesLabel.Text = $"{_trip.StartDate:dd/MM/yyyy HH:mm} al {_trip.EndDate:dd/MM/yyyy HH:mm}";
        PriceLabel.Text = $"{_trip.Price:C} por asiento";
        if (_trip.ChildPrice.HasValue)
            PriceLabel.Text += $" · Niño: {_trip.ChildPrice.Value:C}";
        TransportLabel.Text = TransportTypeConverter.ToDisplay(_trip.TransportType);
        TransportEmojiLabel.Text = _trip.TransportType == TransportType.Camioneta ? "🚐" : "🚌";
        var isCancelled = _trip.CancelledAt.HasValue;
        StatusLabel.Text = isCancelled ? "Cancelado" : (_trip.IsActive ? "Publicado" : "Inactivo");
        StatusBadge.BackgroundColor = isCancelled
            ? Resource("ErrorContainer")
            : _trip.IsActive ? Resource("SuccessContainer") : Resource("SurfaceContainerHigh");
        StatusDot.TextColor = isCancelled
            ? Resource("Error")
            : _trip.IsActive ? Resource("Secondary") : Resource("Outline");
        StatusLabel.TextColor = isCancelled
            ? Resource("OnErrorContainer")
            : _trip.IsActive ? Resource("Secondary") : Resource("Outline");
        PauseButton.Text = _trip.IsActive ? "Pausar" : "Reactivar";
        PauseButton.BackgroundColor = _trip.IsActive ? Resource("WarningContainer") : Resource("SuccessContainer");
        PauseButton.TextColor = _trip.IsActive ? Resource("OnWarningContainer") : Resource("Secondary");
        EditButton.IsVisible = !isCancelled;
        PauseButton.IsVisible = !isCancelled;
        CancelTripButton.IsVisible = !isCancelled && !_trip.Finalized && !_trip.DepartureCompleted;
        FinalizeButton.IsVisible = !isCancelled;
        CheckinButton.IsVisible = !isCancelled;
        if (_trip.CancellationDaysLimit.HasValue)
        {
            CancellationLabel.Text = $"Cancelación del cliente: hasta {_trip.CancellationDaysLimit.Value} día(s) antes de la salida.";
            CancellationLabel.IsVisible = true;
        }
        DescriptionLabel.Text = _trip.Description;

        var sold = _trip.Bookings
            .Where(b => b.Status != BookingStatus.Cancelled)
            .Sum(b => b.NumberOfSeats);

        SoldLabel.Text = sold.ToString();
        AvailableLabel.Text = Math.Max(0, _trip.AvailableSeats).ToString();
        CapacityLabel.Text = _trip.Capacity.ToString();
        HeroCapacityLabel.Text = $"Cupo {_trip.Capacity}";

        FinalizeButton.Text = _trip.Finalized ? "Reabrir viaje (ya finalizado)" : "Finalizar viaje";
        FinalizeButton.BackgroundColor = _trip.Finalized ? Resource("SurfaceContainerHigh") : Resource("PrimaryContainer");
        FinalizeButton.TextColor = _trip.Finalized ? Resource("OnSurfaceVariant") : Resource("OnPrimary");

        RenderCapacityRequests();
        RenderPassengers();
        await RenderRatingsAsync();
        RenderOptions();

        TripImage.Source = _api.GetTripImage(_trip.ImageUrl, ApiService.LargeImageWidth);
        TripImage.IsVisible = TripImage.Source is not null;

        await RenderTransportUnitAsync();
    }

    private async Task RenderTransportUnitAsync()
    {
        if (_trip is null) return;

        try
        {
            _transportInfo = await _api.GetTripTransportAsync(_trip.Id);
        }
        catch
        {
            _transportInfo = null;
        }

        await ApplyTransportUnitAsync(_transportInfo);
    }

    private async Task ApplyTransportUnitAsync(TripTransportInfo? info)
    {
        var hasImage = !string.IsNullOrWhiteSpace(info?.ImageUrl);
        var hasPlate = !string.IsNullOrWhiteSpace(info?.Plate);

        if (hasImage)
        {
            TransportUnitImage.Source = _api.GetTripImage(info!.ImageUrl, ApiService.LargeImageWidth);
            TransportUnitImage.IsVisible = TransportUnitImage.Source is not null;
        }
        else
        {
            TransportUnitImage.Source = null;
            TransportUnitImage.IsVisible = false;
        }

        TransportUnitPlateLabel.Text = hasPlate ? $"Placas: {info!.Plate}" : string.Empty;
        TransportUnitPlateLabel.IsVisible = hasPlate;
        // Estado vacio segun el resultado efectivo de la carga (no solo la URL): si la
        // imagen no se pudo descargar y no hay placas, mostrar el aviso.
        TransportUnitEmptyLabel.IsVisible = TransportUnitImage.Source is null && !hasPlate;
    }

    private void OnAddTransportUnitClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        _selectedImage = null;
        TransportImagePreview.Source = null;
        TransportImagePreview.IsVisible = false;
        TransportImageNameLabel.Text = string.Empty;
        TransportImageNameLabel.IsVisible = false;
        TransportPlateEntry.Text = _transportInfo?.Plate ?? string.Empty;

        TransportUnitEditor.IsVisible = true;
        AddTransportUnitButton.IsVisible = false;
    }

    private async void OnPickTransportImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var results = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
            {
                Title = "Selecciona una foto de la unidad"
            });

            var result = results?.FirstOrDefault();
            if (result is null) return;

            using var stream = await result.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);

            TransportImagePreview.Source = ImageSource.FromStream(() => new MemoryStream(memory.ToArray()));
            TransportImagePreview.IsVisible = true;

            TransportImageNameLabel.Text = result.FileName;
            TransportImageNameLabel.IsVisible = true;

            _selectedImage = result;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnSaveTransportUnitClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        var plate = TransportPlateEntry.Text?.Trim();
        if (_selectedImage is null && string.IsNullOrWhiteSpace(plate))
        {
            await DisplayAlertAsync("Falta información", "Agrega una foto o las placas de la unidad.", "OK");
            return;
        }

        try
        {
            var info = await _api.UploadTripTransportAsync(_trip.Id, _selectedImage, plate);
            if (info is null)
            {
                await DisplayAlertAsync("Error", "No se pudo guardar la información de la unidad.", "OK");
                return;
            }

            _transportInfo = info;
            _selectedImage = null;
            TransportUnitEditor.IsVisible = false;
            AddTransportUnitButton.IsVisible = _session.IsAdmin;

            await ApplyTransportUnitAsync(info);
            await DisplayAlertAsync("Listo", "Información de la unidad guardada.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private void OnCancelTransportUnitClicked(object? sender, EventArgs e)
    {
        _selectedImage = null;
        TransportUnitEditor.IsVisible = false;
        AddTransportUnitButton.IsVisible = _session.IsAdmin;
    }

    private void RenderPassengers()
    {
        var bookings = _trip?.Bookings?
            .OrderByDescending(b => b.BookingDate)
            .ToList() ?? new List<Booking>();

        PassengersHeader.IsVisible = bookings.Count > 0;
        PassengersTotalLabel.IsVisible = bookings.Count > 0;
        PassengersLayout.Children.Clear();

        foreach (var booking in bookings)
        {
            var name = booking.User?.Name ?? booking.User?.Email ?? $"Usuario #{booking.UserId}";

            var (statusTextColor, statusBackground) = booking.Status switch
            {
                BookingStatus.Confirmed => (Resource("Secondary"), Resource("SuccessContainer")),
                BookingStatus.Pending => (Resource("OnWarningContainer"), Resource("WarningContainer")),
                _ => (Resource("Outline"), Resource("SurfaceContainerLow"))
            };

            var statusText = booking.Status switch
            {
                BookingStatus.Confirmed => "Confirmada",
                BookingStatus.Pending => "Pendiente",
                BookingStatus.Cancelled => "Cancelada",
                _ => booking.Status.ToString()
            };

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(42),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 10,
                Padding = new Thickness(0, 3)
            };

            var avatar = new Border
            {
                WidthRequest = 42,
                HeightRequest = 42,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(100) },
                BackgroundColor = Resource("SuccessContainer"),
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = AdminDisplay.Initials(name),
                    FontFamily = "OpenSansSemibold",
                    FontSize = 14,
                    TextColor = Resource("Primary"),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };

            var info = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = name,
                        FontFamily = "OpenSansSemibold",
                        FontSize = 14,
                        TextColor = Resource("OnSurface")
                    },
                    new Label
                    {
                        Text = $"{booking.NumberOfSeats} asiento(s) · {booking.TotalAmount:C}",
                        FontSize = 12,
                        TextColor = Resource("Outline")
                    }
                }
            };

            var statusPill = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(100) },
                BackgroundColor = statusBackground,
                Padding = new Thickness(10, 4),
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = statusText,
                    FontFamily = "OpenSansSemibold",
                    FontSize = 11,
                    TextColor = statusTextColor
                }
            };

            Grid.SetColumn(info, 1);
            Grid.SetColumn(statusPill, 2);
            row.Add(avatar);
            row.Add(info);
            row.Add(statusPill);

            var rowContent = new VerticalStackLayout { Spacing = 8 };
            rowContent.Add(row);

            if (_trip is not null && TripContactPolicy.CanOfferContact(_trip, booking, DateTime.Today))
            {
                var phone = booking.User?.Phone;
                if (AgencyContacts.BuildPhoneUrl(phone) is null)
                {
                    rowContent.Add(new Label
                    {
                        Text = "Sin teléfono registrado",
                        FontSize = 11,
                        TextColor = Resource("Outline"),
                        HorizontalOptions = LayoutOptions.End
                    });
                }
                else
                {
                    var actions = new HorizontalStackLayout
                    {
                        Spacing = 8,
                        HorizontalOptions = LayoutOptions.End
                    };
                    var callButton = new Button
                    {
                        Text = "📞 Llamar",
                        FontSize = 12,
                        HeightRequest = 40,
                        Padding = new Thickness(10, 0),
                        CornerRadius = 10,
                        BackgroundColor = Resource("SurfaceContainerLow"),
                        TextColor = Resource("Primary"),
                        CommandParameter = phone
                    };
                    callButton.Clicked += OnCallClientClicked;

                    var whatsappButton = new Button
                    {
                        Text = "💬 WhatsApp",
                        FontSize = 12,
                        HeightRequest = 40,
                        Padding = new Thickness(10, 0),
                        CornerRadius = 10,
                        BackgroundColor = Resource("SecondaryContainer"),
                        TextColor = Resource("Primary"),
                        CommandParameter = booking
                    };
                    whatsappButton.Clicked += OnWhatsAppClientClicked;

                    actions.Add(callButton);
                    actions.Add(whatsappButton);
                    rowContent.Add(actions);
                }
            }

            PassengersLayout.Children.Add(rowContent);
        }

        var nonCancelled = bookings.Where(b => b.Status != BookingStatus.Cancelled).ToList();
        var totalSeats = nonCancelled.Sum(b => b.NumberOfSeats);
        var totalAmount = nonCancelled.Sum(b => b.TotalAmount);

        PassengersHeader.Text = $"Pasajeros ({bookings.Count})";
        PassengersTotalLabel.Text = $"Total: {totalSeats} asiento(s) vendidos · {totalAmount:C}";
    }

    private async void OnCallClientClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string phone }) return;
        var url = AgencyContacts.BuildPhoneUrl(phone);
        if (url is null) return;

        try
        {
            await Launcher.Default.OpenAsync(url);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo llamar", ex.Message, "OK");
        }
    }

    private async void OnWhatsAppClientClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Booking booking }) return;
        var phone = booking.User?.Phone;
        var greeting = string.IsNullOrWhiteSpace(booking.User?.Name)
            ? "Hola"
            : $"Hola {booking.User.Name}";
        var tripTitle = string.IsNullOrWhiteSpace(_trip?.Title) ? "tu viaje" : _trip.Title;
        var message = $"{greeting}, te contactamos por {tripTitle}.";
        var url = AgencyContacts.BuildWhatsAppUrl(phone, message);
        if (url is null) return;

        try
        {
            await Launcher.Default.OpenAsync(url);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo abrir WhatsApp", ex.Message, "OK");
        }
    }

    private void RenderCapacityRequests()
    {
        var requests = _trip?.CapacityRequests?
            .OrderByDescending(c => c.CreatedAt)
            .ToList() ?? new List<CapacityRequest>();

        CapacityRequestsHeader.IsVisible = requests.Count > 0;
        CapacityRequestsLayout.Children.Clear();

        foreach (var request in requests)
        {
            var name = request.User?.Name ?? request.User?.Email ?? $"Usuario #{request.UserId}";

            var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            header.Add(new Label
            {
                Text = name,
                FontFamily = "OpenSansSemibold",
                FontSize = 13,
                TextColor = Resource("OnSurface"),
                VerticalOptions = LayoutOptions.Center
            });

            var statusPill = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(100) },
                BackgroundColor = request.IsResolved ? Resource("SuccessContainer") : Resource("WarningContainer"),
                Padding = new Thickness(10, 4),
                Content = new Label
                {
                    Text = request.IsResolved ? "Atendida" : "Pendiente",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 10,
                    TextColor = request.IsResolved ? Resource("Secondary") : Resource("OnWarningContainer")
                }
            };
            Grid.SetColumn(statusPill, 1);
            header.Add(statusPill);

            var card = new VerticalStackLayout { Spacing = 4 };
            card.Add(header);
            card.Add(new Label
            {
                Text = $"Pidió {request.RequestedSeats} asiento(s)",
                FontSize = 12,
                TextColor = Resource("OnSurfaceVariant")
            });
            if (!string.IsNullOrWhiteSpace(request.Message))
            {
                card.Add(new Label
                {
                    Text = $"\"{request.Message}\"",
                    FontSize = 12,
                    TextColor = Resource("Outline")
                });
            }

            CapacityRequestsLayout.Children.Add(new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                BackgroundColor = Resource("SurfaceContainerLow"),
                Padding = 12,
                Content = card
            });
        }
    }

    private async Task RenderRatingsAsync()
    {
        if (_trip is null) return;

        var ratings = await _api.GetTripRatingsAsync(_trip.Id) ?? new List<TripRating>();

        RatingsHeader.IsVisible = ratings.Count > 0;
        RatingsAverageLabel.IsVisible = ratings.Count > 0;
        RatingsEmptyLabel.IsVisible = ratings.Count == 0;
        RatingsLayout.Children.Clear();

        if (ratings.Count > 0)
        {
            var average = ratings.Average(r => r.Rating);
            RatingsAverageLabel.Text = $"{ratings.Count} opinión(es) · Promedio: {average:0.0} ★";

            foreach (var rating in ratings)
            {
                var name = rating.UserName ?? $"Usuario #{rating.UserId}";
                RatingsLayout.Children.Add(new Label
                {
                    Text = $"{new string('★', rating.Rating)}{new string('☆', 5 - rating.Rating)} {rating.Rating}/5",
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Resource("Primary")
                });
                RatingsLayout.Children.Add(new Label
                {
                    Text = $"{name} · {rating.CreatedAt:dd/MM/yyyy HH:mm}",
                    FontSize = 12,
                    TextColor = Resource("Outline")
                });
                if (!string.IsNullOrWhiteSpace(rating.Comment))
                {
                    RatingsLayout.Children.Add(new Label
                    {
                        Text = $"\"{rating.Comment}\"",
                        FontSize = 14,
                        TextColor = Resource("OnSurfaceVariant"),
                        Margin = new Thickness(0, 0, 0, 6)
                    });
                }
            }
        }
    }

    private async void OnFinalizeClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        var finalizing = !_trip.Finalized;
        var confirm = await DisplayAlertAsync(
            "Finalizar viaje",
            finalizing
                ? $"¿Finalizar \"{_trip.Title}\"? Se notificará a los clientes con reserva para que califiquen su experiencia."
                : $"¿Reabrir \"{_trip.Title}\"? Se volverá a ocultar el botón de calificación para los clientes.",
            "Sí", "No");

        if (!confirm) return;

        try
        {
            var updated = await _api.UpdateTripFinalizedAsync(_trip.Id, finalizing);
            if (updated is null) return;

            _trip.Finalized = updated.Finalized;
            await LoadTripAsync();

            if (finalizing)
            {
                var count = _trip.Bookings.Count(b => b.Status != BookingStatus.Cancelled);
                await DisplayAlertAsync("Listo", $"Viaje finalizado. Se notificó a los clientes ({count} reserva(s) activas).", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnPaymentsClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"tripbookings?id={_trip.Id}");
    }

    private async void OnMapClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"tripmap?id={_trip.Id}");
    }

    private async void OnAuditClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"auditlog?entity=Trip&id={_trip.Id}");
    }

    private async void OnCheckinClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"checkin?id={_trip.Id}");
    }

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"//admin?editId={_trip.Id}");
    }

    private async void OnPauseClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        var pausing = _trip.IsActive;
        var confirm = await DisplayAlertAsync(
            pausing ? "Pausar viaje" : "Reactivar viaje",
            pausing
                ? $"¿Pausar \"{_trip.Title}\"? Dejará de aparecer para nuevas reservas y se avisará a los clientes con reservas vigentes. Las reservas existentes se mantienen."
                : $"¿Reactivar \"{_trip.Title}\"? Volverá a mostrarse para reservar y se avisará a los clientes con reservas.",
            "Sí", "No");

        if (!confirm) return;

        try
        {
            var updated = await _api.UpdateTripActiveAsync(_trip.Id, !pausing);
            if (updated is null) return;
            _trip.IsActive = updated.IsActive;
            await LoadTripAsync();
            await DisplayAlertAsync("Listo",
                pausing ? "Viaje pausado. Se notificó a los clientes con reservas." : "Viaje reactivado. Se notificó a los clientes con reservas.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnCancelTripClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        var activeBookings = _trip.Bookings
            .Where(b => b.Status is BookingStatus.Pending or BookingStatus.Confirmed)
            .ToList();

        var confirm = await DisplayAlertAsync(
            "Cancelar viaje",
            activeBookings.Count > 0
                ? $"¿Cancelar \"{_trip.Title}\"?\n\nSe cancelarán {activeBookings.Count} reservas y se devolverá el 100% de lo pagado a la wallet de cada cliente. Esta acción no se puede deshacer."
                : $"¿Cancelar \"{_trip.Title}\"? No hay reservas activas; el viaje quedará marcado como cancelado.",
            "Sí, cancelar",
            "No");

        if (!confirm) return;

        try
        {
            var result = await _api.CancelTripAsync(_trip.Id);
            if (result is null) return;

            await DisplayAlertAsync(
                "Viaje cancelado",
                $"Reservas canceladas: {result.BookingsCancelled}\nTotal devuelto a wallet: {result.TotalRefunded:C}",
                "OK");
            await LoadTripAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        var activeBookings = _trip.Bookings
            .Where(b => b.Status is BookingStatus.Pending or BookingStatus.Confirmed)
            .ToList();

        var confirm = await DisplayAlertAsync(
            "Eliminar viaje",
            activeBookings.Count > 0
                ? $"¿Eliminar \"{_trip.Title}\"? Se cancelarán {activeBookings.Count} reservas pendientes o confirmadas y se notificará a esos usuarios. Si hay pagos o reembolsos reales, usa Cancelar viaje."
                : $"¿Eliminar \"{_trip.Title}\"? También se eliminarán sus reservas. Si hay pagos o reembolsos reales, usa Cancelar viaje.",
            "Sí", "No");

        if (!confirm) return;

        string? message = null;
        if (activeBookings.Count > 0)
        {
            message = await DisplayPromptAsync(
                "Mensaje para los afectados",
                $"Los usuarios con reservas pendientes o confirmadas para \"{_trip.Title}\" recibirán este aviso (puedes dejarlo vacío para usar uno automático):",
                accept: "Eliminar y notificar",
                cancel: "Cancelar",
                placeholder: "Tu reserva fue cancelada...");

            if (message is null) return;
        }

        try
        {
            await _api.DeleteTripAsync(_trip.Id, string.IsNullOrWhiteSpace(message) ? null : message);

            if (activeBookings.Count > 0)
                await DisplayAlertAsync("Listo", $"Viaje eliminado. Se notificó a {activeBookings.Count} usuarios con reservas.", "OK");

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de tu cuenta?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }


    private void RenderOptions()
    {
        OptionsLayout.Children.Clear();
        HotelSummaryLabel.Text = _trip?.IncludesHotel == true
            ? $"Incluye hotel · {(_trip.HotelName ?? "sin nombre")} · cupo {_trip.BookableCapacity} lugares"
            : "No incluye hotel · cupo según transporte";

        var options = _trip?.Options?.OrderBy(o => o.Order).ToList() ?? new List<TripOption>();
        OptionsSummaryLabel.Text = options.Count == 0
            ? "Sin opciones: se usa el precio y cupo general del viaje."
            : $"{options.Count} opción(es). Toca \"Administrar opciones\" para editarlas.";

        foreach (var opt in options)
        {
            var state = opt.IsActive ? "Activa" : "Inactiva";
            var child = opt.PriceChild.HasValue ? $"{opt.PriceChild.Value:C} niño" : "niño = adulto";
            OptionsLayout.Children.Add(new Label
            {
                Text = $"{opt.Name} · {opt.PriceAdult:C} adulto · {child} · {state}",
                FontSize = 13,
                TextColor = Resource("OnSurfaceVariant")
            });
        }
    }

    private async void OnManageOptionsClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;
        await Shell.Current.GoToAsync($"tripoptions?id={_trip.Id}");
    }
}
