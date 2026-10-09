using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientProfilePage : ContentPage
{
    const string NotificationsPrefKey = "profile_notifications_enabled";

    /// Numero de WhatsApp de la agencia (52 = Mexico, 445 = Puebla), solo digitos.
    const string AgencyWhatsApp = "5214444579256";

    private readonly ApiService _api;
    private readonly SessionService _session;
    private byte[]? _qrBytes;
    private bool _notificationsEnabled = true;
    private User? _user;
    private decimal _available;

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
            UpdateWalletDisplay(wallet);
        }
        catch
        {
            // Si el endpoint de saldo falla se muestra el saldo en cero.
            UpdateWalletDisplay(null);
        }
    }

    private void UpdateWalletDisplay(WalletSummary? wallet)
    {
        _available = wallet?.Available ?? 0m;
        SaldoLabel.Text = _available.ToString("C");

        var held = wallet?.Held ?? 0m;
        HeldLabel.IsVisible = held > 0;
        HeldLabel.Text = held > 0 ? $"{held:C} en proceso" : string.Empty;
    }

    private async Task LoadProfileAsync()
    {
        var user = await _api.GetMyProfileAsync()
            ?? new User { Name = _session.UserName, Email = null, QrToken = null };

        _user = user;
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

    private async void OnPayoutRequested(object? sender, TappedEventArgs e)
    {
        if (_available <= 0)
        {
            await DisplayAlertAsync("Pedir Reembolso",
                "No tienes saldo disponible para solicitar un reembolso.", "OK");
            return;
        }

        var requestable = _available;
        var input = await DisplayPromptAsync(
            "Pedir Reembolso",
            $"Monto a solicitar. Disponible: {requestable:C}",
            accept: "Solicitar",
            cancel: "Cancelar",
            placeholder: "Monto en MXN",
            keyboard: Keyboard.Numeric,
            initialValue: requestable.ToString("0.##"));
        if (string.IsNullOrWhiteSpace(input)) return;

        if (!decimal.TryParse(input, out var amount) || amount <= 0)
        {
            await DisplayAlertAsync("Monto invalido", "Escribe un monto mayor a cero.", "OK");
            return;
        }

        if (amount > requestable)
        {
            await DisplayAlertAsync("Monto invalido",
                $"El monto supera tu saldo disponible de {requestable:C}.", "OK");
            return;
        }

        try
        {
            var result = await _api.CreatePayoutRequestAsync(amount, null);
            if (result is not null)
                UpdateWalletDisplay(new WalletSummary(result.Balance, result.Held, result.Available, new()));
            else
                UpdateWalletDisplay(await _api.GetWalletAsync());

            await DisplayAlertAsync("Reembolso solicitado",
                $"Registramos tu solicitud de {amount:C}. El saldo queda en proceso y la agencia te contactara.", "OK");

            await OpenPayoutWhatsAppAsync(amount, requestable);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo solicitar", ex.Message, "OK");
        }
    }

    private async Task OpenPayoutWhatsAppAsync(decimal amount, decimal available)
    {
        var name = _user?.Name ?? _session.UserName ?? "Sin nombre";
        var email = string.IsNullOrWhiteSpace(_user?.Email) ? "No registrado" : _user!.Email!;
        var phone = string.IsNullOrWhiteSpace(_user?.Phone) ? "No registrado" : _user!.Phone!;

        var message = "Hola, solicite un reembolso desde la app.\n" +
                      $"Nombre: {name}\n" +
                      $"Correo: {email}\n" +
                      $"Teléfono: {phone}\n" +
                      $"Saldo disponible: {available:C}\n" +
                      $"Monto solicitado: {amount:C}";
        var url = $"https://wa.me/{AgencyWhatsApp}?text={Uri.EscapeDataString(message)}";

        try
        {
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception)
        {
            await DisplayAlertAsync("No se pudo abrir", url, "OK");
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