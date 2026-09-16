using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminDashboardPage : ContentPage
{
    private readonly ApiService _api;

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

            await _api.CreateTripAsync(trip);
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
    }
}