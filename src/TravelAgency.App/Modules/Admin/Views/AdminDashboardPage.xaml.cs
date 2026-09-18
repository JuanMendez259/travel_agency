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
                StartDate = StartDatePicker.Date.GetValueOrDefault(),
                EndDate = EndDatePicker.Date.GetValueOrDefault(),
                Price = decimal.TryParse(PriceEntry.Text, out var price) ? price : 0,
                Capacity = int.TryParse(CapacityEntry.Text, out var capacity) ? capacity : 0,
                TransportType = (TransportType)Math.Max(0, TransportTypePicker.SelectedIndex),
                IsActive = true,
            };

            if (string.IsNullOrEmpty(trip.Title) || string.IsNullOrEmpty(trip.Destination))
            {
                await DisplayAlertAsync("Error", "Título y destino son obligatorios.", "OK");
                return;
            }

            if (trip.EndDate < trip.StartDate)
            {
                await DisplayAlertAsync("Error", "La fecha de fin no puede ser anterior al inicio.", "OK");
                return;
            }

            if (trip.Capacity < 1)
            {
                await DisplayAlertAsync("Error", "La capacidad debe ser al menos 1 asiento.", "OK");
                return;
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
            ResetFormToCreateMode();
            ClearForm();
            await DisplayAlertAsync("Listo", wasEditing ? "Cambios guardados." : "Viaje publicado.", "OK");
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
        if (string.IsNullOrEmpty(imageUrl))
        {
            ImagePreview.IsVisible = false;
            ImageNameLabel.IsVisible = false;
            return;
        }

        ImagePreview.Source = await _api.GetTripImageAsync(imageUrl);
        if (ImagePreview.Source is not null)
        {
            ImagePreview.IsVisible = true;
            ImageNameLabel.Text = _selectedImage is null ? "Imagen actual" : _selectedImage.FileName;
            ImageNameLabel.IsVisible = true;
        }
    }

    private void OnCancelEditClicked(object? sender, EventArgs e)
    {
        ResetFormToCreateMode();
        ClearForm();
    }

    private void StartEdit(Trip trip)
    {
        _editingTrip = trip;
        _selectedImage = null;
        TitleEntry.Text = trip.Title;
        DestinationEntry.Text = trip.Destination;
        DescriptionEditor.Text = trip.Description;
        StartDatePicker.Date = trip.StartDate;
        EndDatePicker.Date = trip.EndDate;
        PriceEntry.Text = trip.Price.ToString();
        CapacityEntry.Text = trip.Capacity.ToString();
        TransportTypePicker.SelectedIndex = (int)trip.TransportType;

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

    private void ClearForm()
    {
        TitleEntry.Text = string.Empty;
        DestinationEntry.Text = string.Empty;
        PriceEntry.Text = string.Empty;
        CapacityEntry.Text = string.Empty;
        DescriptionEditor.Text = string.Empty;
        TransportTypePicker.SelectedIndex = 0;
        _selectedImage = null;
        ImagePreview.Source = null;
        ImagePreview.IsVisible = false;
        ImageNameLabel.Text = string.Empty;
        ImageNameLabel.IsVisible = false;
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
            ImageNameLabel.Text = result.FileName;
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
}