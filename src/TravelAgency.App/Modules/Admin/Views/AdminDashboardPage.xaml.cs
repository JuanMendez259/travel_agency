using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminDashboardPage : ContentPage
{
    private readonly ApiService _api;
    private FileResult? _selectedImage;

    public AdminDashboardPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadMyTripsAsync();
    }

    private async Task LoadMyTripsAsync()
    {
        MyTripsList.ItemsSource = await _api.GetTripsAsync();
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
                AvailableSeats = int.TryParse(SeatsEntry.Text, out var seats) ? seats : 0,
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

            var created = await _api.CreateTripAsync(trip);

            if (_selectedImage is not null && created is not null)
            {
                var updated = await _api.UploadTripImageAsync(created.Id, _selectedImage);
                if (updated?.ImageUrl is not null)
                {
                    created.ImageUrl = updated.ImageUrl;
                }
            }

            ClearForm();
            await LoadMyTripsAsync();
            await DisplayAlertAsync("Listo", "Viaje publicado.", "OK");
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

    private void ClearForm()
    {
        TitleEntry.Text = string.Empty;
        DestinationEntry.Text = string.Empty;
        PriceEntry.Text = string.Empty;
        SeatsEntry.Text = string.Empty;
        DescriptionEditor.Text = string.Empty;
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