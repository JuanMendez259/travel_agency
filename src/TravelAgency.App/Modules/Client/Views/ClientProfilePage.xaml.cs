using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientProfilePage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private byte[]? _qrBytes;

    public ClientProfilePage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadProfileAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar tu perfil: {ex.Message}", "OK");
        }
    }

    private async Task LoadProfileAsync()
    {
        var user = await _api.GetMyProfileAsync()
            ?? new User { Name = _session.UserName, Email = null, QrToken = null };

        NameLabel.Text = user.Name ?? _session.UserName ?? "Usuario";
        EmailLabel.Text = user.Email ?? "Correo no disponible";

        if (NameLabel.Text is { Length: > 0 })
        {
            var parts = NameLabel.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var initials = string.Join("", parts.Select(p => p[0]))[..Math.Min(2, parts.Length)];
            AvatarLabel.Text = initials.ToUpperInvariant();
        }

        ProfileNameValueLabel.Text = user.Name ?? "No registrado";
        ProfileEmailValueLabel.Text = user.Email ?? "No registrado";
        ProfilePhoneValueLabel.Text = string.IsNullOrWhiteSpace(user.Phone) ? "No registrado" : user.Phone;
        ProfileEmergencyValueLabel.Text = string.IsNullOrWhiteSpace(user.EmergencyContact) ? "No registrado" : user.EmergencyContact;

        _qrBytes = QrCodeService.PngBytes(user.QrToken);
        QrImage.Source = _qrBytes is null ? null : ImageSource.FromStream(() => new MemoryStream(_qrBytes));
        QrImage.IsVisible = _qrBytes is not null;
        SavePassButton.IsEnabled = _qrBytes is not null;
        SharePassButton.IsEnabled = _qrBytes is not null;

        try
        {
            var stats = await _api.GetMyProfileStatsAsync();
            TripsCountLabel.Text = (stats?.Trips ?? 0).ToString();
            ReviewsCountLabel.Text = (stats?.Reviews ?? 0).ToString();
        }
        catch
        {
            // Los contadores se quedan en 0 si el endpoint falla.
        }
    }

    private async void OnSavePassClicked(object? sender, EventArgs e)
    {
        if (_qrBytes is null) return;

        SavePassButton.IsEnabled = false;
        try
        {
            await PhotoSaverService.SavePngAsync(_qrBytes, $"pase_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            await DisplayAlertAsync("Pase guardado", "Tu pase QR se guardó en la galería de fotos.", "OK");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo guardar",
                $"No se pudo guardar el pase: {ex.Message}. Revisa el permiso de fotos en Ajustes.", "OK");
        }
        finally
        {
            SavePassButton.IsEnabled = _qrBytes is not null;
        }
    }

    private async void OnSharePassClicked(object? sender, EventArgs e)
    {
        if (_qrBytes is null) return;

        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, $"pase_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            await File.WriteAllBytesAsync(path, _qrBytes);
            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Mi pase QR",
                File = new ShareFile(path, "image/png")
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo compartir", $"No se pudo compartir el pase: {ex.Message}", "OK");
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de tu cuenta?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}