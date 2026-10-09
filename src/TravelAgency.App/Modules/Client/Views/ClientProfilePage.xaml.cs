using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientProfilePage : ContentPage
{
    const string NotificationsPrefKey = "profile_notifications_enabled";

    private readonly ApiService _api;
    private readonly SessionService _session;
    private byte[]? _qrBytes;
    private bool _notificationsEnabled = true;

    public ClientProfilePage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        VersionLabel.Text = $"v{AppInfo.Version} (Build {AppInfo.BuildString}) • RutaVerde México";
        try
        {
            await LoadProfileAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar tu perfil: {ex.Message}", "OK");
        }

        try
        {
            var wallet = await _api.GetWalletAsync();
            SaldoLabel.Text = (wallet?.Balance ?? 0m).ToString("C");
        }
        catch
        {
            // Si el endpoint de saldo falla se muestra el saldo en cero.
            SaldoLabel.Text = "$0.00 MXN";
        }
    }

    private async Task LoadProfileAsync()
    {
        var user = await _api.GetMyProfileAsync()
            ?? new User { Name = _session.UserName, Email = null, QrToken = null };

        NameLabel.Text = user.Name ?? _session.UserName ?? "Usuario";

        if (NameLabel.Text is { Length: > 0 })
        {
            var parts = NameLabel.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var initials = string.Join("", parts.Select(p => p[0]))[..Math.Min(2, parts.Length)];
            AvatarLabel.Text = initials.ToUpperInvariant();
        }

        ProfileNameValueLabel.Text = user.Name ?? "No registrado";
        ProfileEmailValueLabel.Text = string.IsNullOrWhiteSpace(user.Email) ? "No registrado" : user.Email;
        ProfilePhoneValueLabel.Text = string.IsNullOrWhiteSpace(user.Phone) ? "No registrado" : user.Phone;
        ProfileEmergencyValueLabel.Text = string.IsNullOrWhiteSpace(user.EmergencyContact) ? "No registrado" : user.EmergencyContact;

        EmailVerifiedBadge.IsVisible = !string.IsNullOrWhiteSpace(user.Email);
        PhoneVerifiedBadge.IsVisible = !string.IsNullOrWhiteSpace(user.Phone);

        _qrBytes = QrCodeService.PngBytes(user.QrToken);
        QrImage.Source = _qrBytes is null ? null : ImageSource.FromStream(() => new MemoryStream(_qrBytes));
        QrImage.IsVisible = _qrBytes is not null;
        SavePassButton.IsEnabled = _qrBytes is not null;
        SharePassButton.IsEnabled = _qrBytes is not null;
        LoadPassIdentity(user);
        LoadNotificationsPreference();

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

    void LoadPassIdentity(User user)
    {
        var fullName = user.Name ?? _session.UserName;
        QrNameLabel.Text = fullName ?? "Viajero";

        var token = user.QrToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            QrIdLabel.Text = "ID: —";
            return;
        }

        var code = new string(token.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        QrIdLabel.Text = $"ID: RV-MX-{(code.Length > 6 ? code[..6] : code)}";
    }

    void LoadNotificationsPreference()
    {
        _notificationsEnabled = Preferences.Default.Get(NotificationsPrefKey, true);
        NotificationsSwitch.IsToggled = _notificationsEnabled;
    }

    void OnNotificationsToggled(object? sender, ToggledEventArgs e)
    {
        _notificationsEnabled = e.Value;
        Preferences.Default.Set(NotificationsPrefKey, e.Value);
    }

    async void OnComingSoonTapped(object? sender, TappedEventArgs e)
    {
        var feature = (sender as Element)?.ClassId;
        if (string.IsNullOrWhiteSpace(feature))
        {
            feature = "Esta opción";
        }

        await DisplayAlertAsync("Próximamente",
            $"{feature} estará disponible pronto. Por ahora esta función aún no está activa.", "OK");
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