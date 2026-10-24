using System.Collections.ObjectModel;
using TravelAgency.App.Converters;
using TravelAgency.App.Modules.Admin.ViewModels;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminDashboardPage : ContentPage, IQueryAttributable
{
    private readonly ApiService _api;
    private readonly ObservableCollection<AdminTripImageItem> _tripImages = new();
    private readonly List<int> _deletedImageIds = new();
    private Trip? _editingTrip;

    public AdminDashboardPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
        TripImagesList.ItemsSource = _tripImages;
        UpdateImageCounter();
        TransportTypePicker.ItemsSource = TransportTypeConverter.Options.ToList();
        TransportTypePicker.SelectedIndex = 0;
        CategoryPicker.ItemsSource = TripCategoryOptions.PickerOptions
            .Select(c => string.IsNullOrEmpty(c) ? "Sin categoría" : c)
            .ToList();
        CategoryPicker.SelectedIndex = 0;
        StartTimePicker.Time = new TimeSpan(8, 0, 0);
        EndTimePicker.Time = new TimeSpan(18, 0, 0);

        // En modo creación el viaje nace activo; mantener switch y etiqueta consistentes.
        ActiveSwitch.IsToggled = true;
        ApplyActiveState();
        ApplyTransportFloorVisibility();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("editId", out var value) && int.TryParse(value?.ToString(), out var id))
        {
            _ = LoadForEditAsync(id);
        }
    }

    private async Task LoadForEditAsync(int id)
    {
        try
        {
            var trips = await _api.GetAdminTripsAsync();
            var trip = trips?.FirstOrDefault(t => t.Id == id);
            if (trip is null) return;

            StartEdit(trip);
            LoadTripImages(trip);
            await PageScroll.ScrollToAsync(0, 0, true);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            var twoFloors = TwoFloorsSwitch.IsToggled && TransportTypePicker.SelectedIndex == (int)TransportType.Camion;
            var floor1 = int.TryParse(Floor1CapacityEntry.Text, out var f1) ? f1 : (int?)null;
            var floor2 = int.TryParse(Floor2CapacityEntry.Text, out var f2) ? f2 : (int?)null;

            var trip = new Trip
            {
                Title = TitleEntry.Text?.Trim(),
                Destination = DestinationEntry.Text?.Trim(),
                Description = DescriptionEditor.Text?.Trim(),
                StartDate = StartDatePicker.Date.GetValueOrDefault().Date + (StartTimePicker.Time ?? TimeSpan.Zero),
                EndDate = EndDatePicker.Date.GetValueOrDefault().Date + (EndTimePicker.Time ?? TimeSpan.Zero),
                Price = decimal.TryParse(PriceEntry.Text, out var price) ? price : 0,
                ChildPrice = decimal.TryParse(ChildPriceEntry.Text, out var childPrice) ? childPrice : (decimal?)null,
                Capacity = twoFloors
                    ? (floor1 ?? 0) + (floor2 ?? 0)
                    : (int.TryParse(CapacityEntry.Text, out var capacity) ? capacity : 0),
                TransportType = (TransportType)Math.Max(0, TransportTypePicker.SelectedIndex),
                Category = TripCategoryOptions.FromIndex(CategoryPicker.SelectedIndex),
                IsActive = ActiveSwitch.IsToggled,
                CancellationDaysLimit = int.TryParse(CancellationDaysLimitEntry.Text, out var cancelDays) && cancelDays >= 0
                    ? cancelDays
                    : (int?)null,
                BookingDeadline = BookingDeadlineSwitch.IsToggled
                    ? BookingDeadlinePicker.Date.GetValueOrDefault().Date
                    : null,
                IncludesHotel = IncludesHotelSwitch.IsToggled,
                HotelName = string.IsNullOrWhiteSpace(HotelNameEntry.Text) ? null : HotelNameEntry.Text.Trim(),
                HotelCapacity = int.TryParse(HotelCapacityEntry.Text, out var hotelCap) ? hotelCap : (int?)null,
                HasTwoFloors = twoFloors,
                Floor1Capacity = twoFloors ? floor1 : null,
                Floor2Capacity = twoFloors ? floor2 : null,
            };

            if (string.IsNullOrEmpty(trip.Title) || string.IsNullOrEmpty(trip.Destination))
            {
                await DisplayAlertAsync("Error", "Título y destino son obligatorios.", "OK");
                return;
            }

            if (trip.EndDate <= trip.StartDate)
            {
                await DisplayAlertAsync("Error", "La fecha y hora de fin no puede ser anterior (o igual) a la de inicio.", "OK");
                return;
            }

            if (twoFloors && (floor1 is null || floor1 < 1))
            {
                await DisplayAlertAsync("Error", "El cupo del piso 1 debe ser al menos 1.", "OK");
                Floor1CapacityEntry.Focus();
                return;
            }

            if (twoFloors && (floor2 is null || floor2 < 1))
            {
                await DisplayAlertAsync("Error", "El cupo del piso 2 debe ser al menos 1.", "OK");
                Floor2CapacityEntry.Focus();
                return;
            }

            if (trip.Capacity < 1)
            {
                await DisplayAlertAsync("Error", "La capacidad debe ser de al menos 1 asiento.", "OK");
                return;
            }

            if (trip.StartDate <= DateTime.Now)
            {
                await DisplayAlertAsync("Error", "La fecha y hora de salida no pueden estar en el pasado.", "OK");
                return;
            }

            if (trip.Price < 0)
            {
                await DisplayAlertAsync("Error", "El precio no puede ser negativo.", "OK");
                return;
            }

            if (trip.ChildPrice < 0)
            {
                await DisplayAlertAsync("Error", "El precio de niño no puede ser negativo.", "OK");
                return;
            }

            if (trip.CancellationDaysLimit is null)
            {
                await DisplayAlertAsync("Error",
                    $"Define los días de cancelación del cliente (por ejemplo {BookingRefundPolicy.DefaultCancellationDaysLimit}). Es obligatorio para que pueda cancelar desde la app.", "OK");
                CancellationDaysLimitEntry.Focus();
                return;
            }

            if (trip.BookingDeadline is { } deadline)
            {
                if (deadline.Date > trip.StartDate.Date)
                {
                    await DisplayAlertAsync("Error", "La fecha límite de reserva no puede ser posterior a la fecha de salida.", "OK");
                    return;
                }

                if (deadline.Date < DateTime.Today)
                {
                    await DisplayAlertAsync("Error", "La fecha límite de reserva no puede estar en el pasado.", "OK");
                    return;
                }
            }

            if (trip.IncludesHotel && (trip.HotelCapacity is null || trip.HotelCapacity < 1))
            {
                await DisplayAlertAsync("Error", "Indica cuántos lugares hay disponibles según el hotel (mínimo 1).", "OK");
                HotelCapacityEntry.Focus();
                return;
            }


            var wasEditing = _editingTrip is not null;
            Trip? savedTrip;

            if (_editingTrip is not null)
            {
                savedTrip = await _api.UpdateTripAsync(_editingTrip.Id, trip);
                if (savedTrip is null)
                {
                    await DisplayAlertAsync("Error", "No se pudo guardar el viaje. Intenta de nuevo.", "OK");
                    return;
                }

                _editingTrip = savedTrip;
            }
            else
            {
                savedTrip = await _api.CreateTripAsync(trip);
                if (savedTrip is null)
                {
                    await DisplayAlertAsync("Error", "No se pudo publicar el viaje. Intenta de nuevo.", "OK");
                    return;
                }

                // El viaje ya existe. Cambiar a modo edicion antes de tocar las
                // imagenes evita duplicarlo si la sincronizacion falla.
                _editingTrip = savedTrip;
                ApplyEditModeLabels();
            }

            var synced = await SyncTripImagesAsync(savedTrip.Id, savedTrip);
            if (synced is null)
            {
                await DisplayAlertAsync("Imágenes sin guardar",
                    "El viaje quedó guardado, pero no se pudieron sincronizar las imágenes. Revisa tu conexión e intenta guardar de nuevo.",
                    "OK");
                return;
            }

            await DisplayAlertAsync("Listo", wasEditing ? "Cambios guardados." : "Viaje publicado.", "OK");
            ResetFormToCreateMode();
            await ClearFormAsync();
            await Shell.Current.GoToAsync("//admintrips");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async void OnCancelEditClicked(object? sender, EventArgs e)
    {
        ResetFormToCreateMode();
        await ClearFormAsync();
    }

    private void UpdateCancellationHint()
    {
        CancellationHintLabel.Text = int.TryParse(CancellationDaysLimitEntry.Text, out var days) && days >= 0
            ? $"Dentro de {days} día(s) antes de la salida el reembolso es del 100%. Fuera de ese rango se aplica multa del 30%."
            : $"Obligatorio. Ej: {BookingRefundPolicy.DefaultCancellationDaysLimit} días antes de la salida. Dentro del rango el reembolso es del 100%; fuera, multa del 30%.";
    }

    private void StartEdit(Trip trip)
    {
        _editingTrip = trip;
        TitleEntry.Text = trip.Title;
        DestinationEntry.Text = trip.Destination;
        DescriptionEditor.Text = trip.Description;
        StartDatePicker.Date = trip.StartDate;
        StartTimePicker.Time = trip.StartDate.TimeOfDay;
        EndDatePicker.Date = trip.EndDate;
        EndTimePicker.Time = trip.EndDate.TimeOfDay;
        PriceEntry.Text = trip.Price.ToString();
        ChildPriceEntry.Text = trip.ChildPrice?.ToString();
        CapacityEntry.Text = trip.Capacity.ToString();
        CancellationDaysLimitEntry.Text = trip.CancellationDaysLimit?.ToString();
        UpdateCancellationHint();
        BookingDeadlineSwitch.IsToggled = trip.BookingDeadline.HasValue;
        if (trip.BookingDeadline is { } editDeadline)
            BookingDeadlinePicker.Date = editDeadline;
        ApplyBookingDeadlineState();
        TransportTypePicker.SelectedIndex = (int)trip.TransportType;
        CategoryPicker.SelectedIndex = TripCategoryOptions.IndexOf(trip.Category);
        ActiveSwitch.IsToggled = trip.IsActive;
        ApplyActiveState();

        TwoFloorsSwitch.IsToggled = trip.HasTwoFloors;
        Floor1CapacityEntry.Text = trip.Floor1Capacity?.ToString() ?? string.Empty;
        Floor2CapacityEntry.Text = trip.Floor2Capacity?.ToString() ?? string.Empty;
        UpdateFloorsTotal();

        IncludesHotelSwitch.IsToggled = trip.IncludesHotel;
        HotelNameEntry.Text = trip.HotelName ?? string.Empty;
        HotelCapacityEntry.Text = trip.HotelCapacity?.ToString() ?? string.Empty;
        ApplyHotelState();

        ApplyEditModeLabels();
    }

    private void ApplyEditModeLabels()
    {
        FormTitleLabel.Text = "Editar viaje";
        SaveButton.Text = "Guardar cambios";
        CancelEditButton.IsVisible = true;
    }

    private void ResetFormToCreateMode()
    {
        _editingTrip = null;
        FormTitleLabel.Text = "Nuevo viaje";
        SaveButton.Text = "Guardar viaje";
        CancelEditButton.IsVisible = false;
    }

    private async Task ClearFormAsync()
    {
        TitleEntry.Text = string.Empty;
        DestinationEntry.Text = string.Empty;
        PriceEntry.Text = string.Empty;
        ChildPriceEntry.Text = string.Empty;
        CapacityEntry.Text = string.Empty;
        CancellationDaysLimitEntry.Text = BookingRefundPolicy.DefaultCancellationDaysLimit.ToString();
        UpdateCancellationHint();
        BookingDeadlineSwitch.IsToggled = false;
        ApplyBookingDeadlineState();
        StartTimePicker.Time = new TimeSpan(8, 0, 0);
        EndTimePicker.Time = new TimeSpan(18, 0, 0);
        DescriptionEditor.Text = string.Empty;
        TransportTypePicker.SelectedIndex = 0;
        CategoryPicker.SelectedIndex = 0;
        ActiveSwitch.IsToggled = true;
        ApplyActiveState();
        TwoFloorsSwitch.IsToggled = false;
        Floor1CapacityEntry.Text = string.Empty;
        Floor2CapacityEntry.Text = string.Empty;
        UpdateFloorsTotal();

        IncludesHotelSwitch.IsToggled = false;
        HotelNameEntry.Text = string.Empty;
        HotelCapacityEntry.Text = string.Empty;
        ApplyHotelState();

        ClearTripImages();
    }

    private void OnActiveToggled(object? sender, ToggledEventArgs e)
    {
        ApplyActiveState();
    }

    private void ApplyActiveState()
    {
        var active = ActiveSwitch.IsToggled;
        ActiveStateLabel.Text = active ? "Activo" : "Pausado";
        ActiveStateLabel.TextColor = active
            ? Color.FromArgb("#003B1B")
            : Color.FromArgb("#BA1A1A");
        ActiveHintLabel.Text = active
            ? "Los clientes podrán verlo y reservar."
            : "No aparecerá para nuevas reservas; las existentes se mantienen.";
    }

    private void OnTransportChanged(object? sender, EventArgs e)
    {
        ApplyTransportFloorVisibility();
    }

    private void ApplyTransportFloorVisibility()
    {
        var isCamion = TransportTypePicker.SelectedIndex == (int)TransportType.Camion;
        TwoFloorsSection.IsVisible = isCamion;
        if (!isCamion)
        {
            TwoFloorsSwitch.IsToggled = false;
            FloorFieldsLayout.IsVisible = false;
            SingleCapacityLayout.IsVisible = true;
        }
    }

    private void OnTwoFloorsToggled(object? sender, ToggledEventArgs e)
    {
        FloorFieldsLayout.IsVisible = e.Value;
        SingleCapacityLayout.IsVisible = !e.Value;
        if (!e.Value)
        {
            Floor1CapacityEntry.Text = string.Empty;
            Floor2CapacityEntry.Text = string.Empty;
        }
        UpdateFloorsTotal();
    }

    private void OnFloorCapacityChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateFloorsTotal();
    }

    private void UpdateFloorsTotal()
    {
        var f1 = int.TryParse(Floor1CapacityEntry.Text, out var a) ? a : 0;
        var f2 = int.TryParse(Floor2CapacityEntry.Text, out var b) ? b : 0;
        FloorsTotalLabel.Text = $"Total: {Math.Max(0, f1 + f2)} asientos";
    }

    private void OnIncludesHotelToggled(object? sender, ToggledEventArgs e)
    {
        ApplyHotelState();
    }

    private void ApplyHotelState()
    {
        var enabled = IncludesHotelSwitch.IsToggled;
        HotelFieldsLayout.IsVisible = enabled;
        if (!enabled)
        {
            HotelNameEntry.Text = string.Empty;
            HotelCapacityEntry.Text = string.Empty;
        }
    }

    private void OnBookingDeadlineToggled(object? sender, ToggledEventArgs e)
    {
        ApplyBookingDeadlineState();
    }

    private void ApplyBookingDeadlineState()
    {
        var enabled = BookingDeadlineSwitch.IsToggled;
        BookingDeadlinePicker.IsVisible = enabled;
        BookingDeadlineHintLabel.Text = enabled
            ? "Se puede reservar hasta este día inclusive."
            : "Actívalo para cerrar las reservas antes de la salida.";

        if (enabled && BookingDeadlinePicker.Date.GetValueOrDefault().Date < DateTime.Today)
        {
            BookingDeadlinePicker.Date = StartDatePicker.Date.GetValueOrDefault().AddDays(-1);
        }
    }

    private void LoadTripImages(Trip trip)
    {
        _tripImages.Clear();
        _deletedImageIds.Clear();

        var ordered = (trip.Images ?? new List<TripImage>())
            .OrderBy(i => i.Order)
            .ThenBy(i => i.Id)
            .ToList();

        foreach (var image in ordered)
        {
            _tripImages.Add(new AdminTripImageItem
            {
                Id = image.Id,
                Url = image.Url,
                Source = _api.GetTripImage(image.Url, ApiService.LargeImageWidth),
                IsCover = image.IsCover,
                Order = _tripImages.Count + 1
            });
        }

        // Viajes anteriores a la galeria: la portada vive solo en Trip.ImageUrl.
        // Se muestra como referencia y no se puede editar desde aqui.
        if (_tripImages.Count == 0 &&
            !string.IsNullOrWhiteSpace(trip.ImageUrl) &&
            !trip.ImageUrl.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            _tripImages.Add(new AdminTripImageItem
            {
                Url = trip.ImageUrl,
                Source = _api.GetTripImage(trip.ImageUrl, ApiService.LargeImageWidth),
                Order = 1
            });
        }

        EnsureCover();
        UpdateImageCounter();
    }

    private void ClearTripImages()
    {
        _tripImages.Clear();
        _deletedImageIds.Clear();
        UpdateImageCounter();
    }

    private async void OnPickImagesClicked(object? sender, EventArgs e)
    {
        try
        {
            var remaining = TripImageRules.MaxPerTrip - _tripImages.Count;
            if (remaining <= 0)
            {
                await DisplayAlertAsync("Límite alcanzado",
                    $"Un viaje puede tener hasta {TripImageRules.MaxPerTrip} imágenes. Elimina alguna para agregar otra.",
                    "OK");
                return;
            }

            var results = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
            {
                Title = "Selecciona imágenes del viaje",
                SelectionLimit = remaining,
                MaximumWidth = 1920,
                MaximumHeight = 1920,
                CompressionQuality = 80,
                RotateImage = true
            });

            if (results is null || results.Count == 0) return;

            var invalid = new List<string>();
            foreach (var result in results)
            {
                if (_tripImages.Count >= TripImageRules.MaxPerTrip)
                    break;

                using var stream = await result.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                var bytes = memory.ToArray();

                if (!TripImageFileRules.IsAllowed(result.FileName, bytes.Length))
                {
                    var extension = Path.GetExtension(result.FileName);
                    var reason = !TripImageFileRules.AllowedExtensions.Contains(extension)
                        ? "formato no permitido"
                        : $"supera {TripImageFileRules.MaxInputBytes / (1024 * 1024)} MB";
                    invalid.Add($"{result.FileName} ({reason}, {FormatFileSize(bytes.Length)})");
                    continue;
                }

                _tripImages.Add(new AdminTripImageItem
                {
                    File = result,
                    PreviewBytes = bytes,
                    Source = ImageSource.FromStream(() => new MemoryStream(bytes)),
                    Order = _tripImages.Count + 1
                });
            }

            EnsureCover();
            ReassignOrder();
            UpdateImageCounter();

            if (invalid.Count > 0)
            {
                await DisplayAlertAsync("Imágenes omitidas",
                    "No se agregaron:\n" + string.Join("\n", invalid) +
                    $"\n\nUsa JPG, PNG o WebP de hasta {TripImageFileRules.MaxInputBytes / (1024 * 1024)} MB.",
                    "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private void OnRemoveImageClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not AdminTripImageItem item) return;
        if (!item.IsEditable) return;

        if (item.Id is int id && !_deletedImageIds.Contains(id))
            _deletedImageIds.Add(id);

        item.IsDeleted = true;
        _tripImages.Remove(item);
        EnsureCover();
        ReassignOrder();
        UpdateImageCounter();
    }

    private void OnMoveImageLeftClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not AdminTripImageItem item) return;
        if (!item.IsEditable) return;

        var index = _tripImages.IndexOf(item);
        if (index <= 0) return;
        if (!_tripImages[index - 1].IsEditable) return;

        _tripImages.Move(index, index - 1);
        ReassignOrder();
    }

    private void OnMoveImageRightClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not AdminTripImageItem item) return;
        if (!item.IsEditable) return;

        var index = _tripImages.IndexOf(item);
        if (index < 0 || index >= _tripImages.Count - 1) return;
        if (!_tripImages[index + 1].IsEditable) return;

        _tripImages.Move(index, index + 1);
        ReassignOrder();
    }

    private void OnSetCoverClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not AdminTripImageItem item) return;
        if (!item.IsEditable) return;

        foreach (var image in _tripImages)
            image.IsCover = ReferenceEquals(image, item);
    }

    private void EnsureCover()
    {
        var editable = _tripImages.Where(i => i.IsEditable).ToList();
        if (editable.Count == 0)
        {
            foreach (var image in _tripImages) image.IsCover = false;
            return;
        }

        var cover = editable.FirstOrDefault(i => i.IsCover) ?? editable[0];
        foreach (var image in _tripImages)
            image.IsCover = ReferenceEquals(image, cover);
    }

    private void ReassignOrder()
    {
        for (var index = 0; index < _tripImages.Count; index++)
            _tripImages[index].Order = index + 1;
    }

    private void UpdateImageCounter()
    {
        ImageCounterLabel.Text = $"{_tripImages.Count} / {TripImageRules.MaxPerTrip}";
    }

    private async Task<Trip?> SyncTripImagesAsync(int tripId, Trip savedTrip)
    {
        try
        {
            ReassignOrder();

            var newItems = _tripImages.Where(i => i.IsNew && !i.IsDeleted).ToList();
            if (newItems.Count > 0)
            {
                var upload = await _api.UploadTripImagesAsync(tripId, newItems.Select(i => i.File!).ToList());
                if (upload is null)
                    return null;

                // Marca las que el servidor ya creo antes de decidir si hubo un
                // fallo: asi un reintento no las vuelve a subir.
                var uploadedCount = Math.Min(upload.Uploaded.Count, newItems.Count);
                for (var index = 0; index < uploadedCount; index++)
                {
                    var uploaded = upload.Uploaded[index];
                    newItems[index].MarkUploaded(uploaded.Id, uploaded.Url);
                }

                if (upload.Uploaded.Count != newItems.Count)
                    return null;
            }

            var orderedIds = _tripImages
                .Where(i => !i.IsDeleted && i.Id.HasValue)
                .OrderBy(i => i.Order)
                .Select(i => i.Id!.Value)
                .ToList();

            var current = savedTrip;
            if (orderedIds.Count > 1)
            {
                current = await _api.ReorderTripImagesAsync(tripId, orderedIds) ?? current;
            }

            var coverId = _tripImages
                .Where(i => i.IsCover && !i.IsDeleted && i.Id.HasValue)
                .OrderBy(i => i.Order)
                .Select(i => i.Id)
                .FirstOrDefault();

            var serverCoverId = current?.Images?.FirstOrDefault(i => i.IsCover)?.Id;
            if (coverId is int cover && cover != serverCoverId)
            {
                current = await _api.SetTripImageCoverAsync(tripId, cover) ?? current;
            }

            foreach (var imageId in _deletedImageIds.Distinct())
            {
                await _api.DeleteTripImageAsync(tripId, imageId);
            }

            return await _api.GetTripAsync(tripId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncTripImagesAsync] {ex}");
            return null;
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }

    private static string FormatFileSize(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024):0.#} MB"
            : $"{bytes / 1024.0:0.#} KB";
}