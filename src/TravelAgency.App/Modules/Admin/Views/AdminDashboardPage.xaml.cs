using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminDashboardPage : ContentPage, IQueryAttributable
{
    private readonly ApiService _api;
    private FileResult? _selectedImage;
    private Trip? _editingTrip;

    public AdminDashboardPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
        TransportTypePicker.ItemsSource = TransportTypeConverter.Options.ToList();
        TransportTypePicker.SelectedIndex = 0;
        CategoryPicker.ItemsSource = TripCategoryOptions.PickerOptions
            .Select(c => string.IsNullOrEmpty(c) ? "Sin categoría" : c)
            .ToList();
        CategoryPicker.SelectedIndex = 0;
        StartTimePicker.Time = new TimeSpan(8, 0, 0);
        EndTimePicker.Time = new TimeSpan(18, 0, 0);
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
            await LoadPreviewAsync(trip.ImageUrl);
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
            var trip = new Trip
            {
                Title = TitleEntry.Text?.Trim(),
                Destination = DestinationEntry.Text?.Trim(),
                Description = DescriptionEditor.Text?.Trim(),
                StartDate = StartDatePicker.Date.GetValueOrDefault().Date + (StartTimePicker.Time ?? TimeSpan.Zero),
                EndDate = EndDatePicker.Date.GetValueOrDefault().Date + (EndTimePicker.Time ?? TimeSpan.Zero),
                Price = decimal.TryParse(PriceEntry.Text, out var price) ? price : 0,
                ChildPrice = decimal.TryParse(ChildPriceEntry.Text, out var childPrice) ? childPrice : (decimal?)null,
                Capacity = int.TryParse(CapacityEntry.Text, out var capacity) ? capacity : 0,
                TransportType = (TransportType)Math.Max(0, TransportTypePicker.SelectedIndex),
                Category = TripCategoryOptions.FromIndex(CategoryPicker.SelectedIndex),
                IsActive = ActiveSwitch.IsToggled,
                CancellationDaysLimit = int.TryParse(CancellationDaysLimitEntry.Text, out var cancelDays) && cancelDays >= 0
                    ? cancelDays
                    : (int?)null,
                BookingDeadline = BookingDeadlineSwitch.IsToggled
                    ? BookingDeadlinePicker.Date.GetValueOrDefault().Date
                    : null,
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


            if (_editingTrip is not null)
            {
                var updated = await _api.UpdateTripAsync(_editingTrip.Id, trip);
                if (_selectedImage is not null && updated is not null)
                {
                    updated = await _api.UploadTripImageAsync(updated.Id, _selectedImage);
                }
            }
            else
            {
                var created = await _api.CreateTripAsync(trip);
                if (_selectedImage is not null && created is not null)
                {
                    var updated = await _api.UploadTripImageAsync(created.Id, _selectedImage);
                    if (updated?.ImageUrl is not null)
                    {
                        created.ImageUrl = updated.ImageUrl;
                    }
                }
            }

            var wasEditing = _editingTrip is not null;
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

    private async Task LoadPreviewAsync(string? imageUrl)
    {
        var hasImage = !string.IsNullOrEmpty(imageUrl);

        ImagePreview.Source = await _api.GetTripImageAsync(imageUrl);
        if (ImagePreview.Source is null) return;

        ImagePreview.IsVisible = true;
        ImageNameLabel.Text = hasImage
            ? (_selectedImage is null ? "Imagen actual" : _selectedImage.FileName)
            : "Sin imagen: se usará el placeholder";
        ImageNameLabel.IsVisible = true;
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
        _selectedImage = null;
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

        ImagePreview.Source = null;
        ImagePreview.IsVisible = false;
        ImageNameLabel.IsVisible = false;

        FormTitleLabel.Text = "Editar viaje";
        SaveButton.Text = "Guardar cambios";
        CancelEditButton.IsVisible = true;
    }

    private void ResetFormToCreateMode()
    {
        _editingTrip = null;
        _selectedImage = null;
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
        _selectedImage = null;
        ImageNameLabel.Text = string.Empty;
        ImageNameLabel.IsVisible = false;

        await LoadPreviewAsync(null);
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
            ? (Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.White : Colors.Black)
            : Color.FromArgb("#C53030");
        ActiveHintLabel.Text = active
            ? "Los clientes podrán verlo y reservar."
            : "No aparecerá para nuevas reservas; las existentes se mantienen.";
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

    private async void OnPickImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var results = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
            {
                Title = "Selecciona una imagen del viaje"
            });

            var result = results?.FirstOrDefault();
            if (result is null) return;

            using var stream = await result.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);

            ImagePreview.Source = ImageSource.FromStream(() => new MemoryStream(memory.ToArray()));
            ImagePreview.IsVisible = true;

            var sizeLabel = $"{result.FileName} · {FormatFileSize(memory.Length)}";
            if (memory.Length > 5L * 1024 * 1024)
                sizeLabel += " (se comprimirá al subir)";
            ImageNameLabel.Text = sizeLabel;
            ImageNameLabel.IsVisible = true;

            _selectedImage = result;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
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