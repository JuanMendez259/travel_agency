using TravelAgency.App.Services;
using TravelAgency.Shared.Models;
using ZXing.Net.Maui;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminScanQrPage : ContentPage
{
    private readonly ApiService _api;
    private Trip? _trip;
    private bool _busy;

    public string TripId { get; set; } = string.Empty;

    public AdminScanQrPage(ApiService api)
    {
        InitializeComponent();
        _api = api;

        ScannerView.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormats.TwoDimensional,
            TryHarder = true,
            AutoRotate = true,
            Multiple = false
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadTripAsync();
            var granted = await EnsureCameraPermissionAsync();
            if (granted)
            {
                ScannerView.IsDetecting = true;
            }
            else
            {
                ResultLabel.Text = "Cámara no disponible";
                DetailLabel.Text = "Usa el código del QR en el campo de abajo.";
                DetailLabel.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            ResultLabel.Text = "Error al iniciar la cámara";
            DetailLabel.Text = ex.Message;
            DetailLabel.IsVisible = true;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ScannerView.IsDetecting = false;
    }

    private async Task LoadTripAsync()
    {
        if (!int.TryParse(TripId, out var id)) return;

        var trips = await _api.GetAdminTripsAsync();
        _trip = trips?.FirstOrDefault(t => t.Id == id);
        if (_trip is not null)
            TripLabel.Text = $"{_trip.Title} · {_trip.Destination}";
    }

    private async Task<bool> EnsureCameraPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Camera>();
        }
        return status == PermissionStatus.Granted;
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_busy || !ScannerView.IsDetecting) return;

        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value)) return;

        _busy = true;
        ScannerView.IsDetecting = false;
        Dispatcher.Dispatch(async () => await ProcessTokenAsync(value.Trim()));
    }

    private async void OnManualVerifyClicked(object? sender, EventArgs e)
    {
        if (_busy) return;

        var value = ManualTokenEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            await DisplayAlertAsync("Código vacío", "Pega o escribe el código del QR.", "OK");
            return;
        }

        _busy = true;
        await ProcessTokenAsync(value);
    }

    private async Task ProcessTokenAsync(string token)
    {
        if (_trip is null) return;

        try
        {
            var result = await _api.CheckinByTokenAsync(token, _trip.Id);
            var name = result?.Name ?? "Pasajero";

            if (result?.AlreadyCheckedIn == true)
            {
                ResultLabel.Text = $"{name} ya había abordado";
                ResultLabel.TextColor = Color.FromArgb("#2B6CB0");
            }
            else
            {
                ResultLabel.Text = $"{name} abordó correctamente";
                ResultLabel.TextColor = Color.FromArgb("#2F855A");
            }

            DetailLabel.Text = result?.Kind == "passenger"
                ? "Acompañante registrado en el viaje"
                : $"Reserva registrada · {result?.Seats ?? 1} asiento(s)";
            DetailLabel.IsVisible = true;
        }
        catch (Exception ex)
        {
            ResultLabel.Text = "No se pudo registrar";
            ResultLabel.TextColor = Color.FromArgb("#C53030");
            DetailLabel.Text = ex.Message;
            DetailLabel.IsVisible = true;
        }
        finally
        {
            _busy = false;
            ResumeButton.IsVisible = true;
        }
    }

    private async void OnResumeClicked(object? sender, EventArgs e)
    {
        ResultLabel.Text = "Apunta la cámara al código QR…";
        ResultLabel.TextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.White : Colors.Black;
        DetailLabel.IsVisible = false;
        ResumeButton.IsVisible = false;
        ManualTokenEntry.Text = string.Empty;

        if (await EnsureCameraPermissionAsync())
            ScannerView.IsDetecting = true;
    }
}