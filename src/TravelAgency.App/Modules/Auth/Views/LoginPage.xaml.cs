using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Auth.Views;

public partial class LoginPage : ContentPage
{
    private const string RememberedEmailKey = "login_remembered_email";
    private const string RememberedPasswordKey = "login_remembered_password";

    private readonly ApiService _api;
    private readonly SessionService _session;
    private bool _isRegistering;
    private bool _isPasswordVisible;

    public LoginPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        QuickLoginPicker.ItemsSource = new[] { "Admin", "Coordinador", "Cliente" };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadRememberedAsync();
    }

    private async Task LoadRememberedAsync()
    {
        try
        {
            var email = await SecureStorage.Default.GetAsync(RememberedEmailKey);
            if (string.IsNullOrWhiteSpace(email)) return;

            RememberCheckBox.IsChecked = true;
            EmailEntry.Text = email;
            PasswordEntry.Text = await SecureStorage.Default.GetAsync(RememberedPasswordKey) ?? string.Empty;
        }
        catch (Exception)
        {
            // Sin almacenamiento seguro disponible: el login sigue funcionando.
        }
    }

    private async Task SaveRememberedAsync()
    {
        try
        {
            if (RememberCheckBox.IsChecked == true && !_isRegistering)
            {
                await SecureStorage.Default.SetAsync(RememberedEmailKey, EmailEntry.Text?.Trim() ?? string.Empty);
                await SecureStorage.Default.SetAsync(RememberedPasswordKey, PasswordEntry.Text ?? string.Empty);
            }
            else
            {
                ForgetRemembered();
            }
        }
        catch (Exception)
        {
            // Si no se puede guardar, el login sigue funcionando.
        }
    }

    private static void ForgetRemembered()
    {
        try
        {
            SecureStorage.Default.Remove(RememberedEmailKey);
            SecureStorage.Default.Remove(RememberedPasswordKey);
        }
        catch (Exception)
        {
        }
    }

    private async void OnRememberTapped(object? sender, TappedEventArgs e)
        => RememberCheckBox.IsChecked = RememberCheckBox.IsChecked != true;

    private void OnRememberCheckedChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (e.Value == false)
            ForgetRemembered();
    }

    private async void OnQuickLoginSelected(object? sender, EventArgs e)
    {
        var index = QuickLoginPicker.SelectedIndex;
        if (index < 0) return;

        if (_isRegistering)
            OnToggleClicked(sender, e);

        if (index == 0)
        {
            EmailEntry.Text = "admin@travelagency.com";
            PasswordEntry.Text = "Admin123!";
        }
        else if (index == 1)
        {
            EmailEntry.Text = "coordinador@gmail.com";
            PasswordEntry.Text = "agencia2026";
        }
        else
        {
            EmailEntry.Text = "cliente@travelagency.com";
            PasswordEntry.Text = "Cliente123!";
        }

        OnSubmitClicked(sender, e);
    }

    private void OnToggleClicked(object? sender, EventArgs e)
    {
        _isRegistering = !_isRegistering;

        FormTitleLabel.Text = _isRegistering ? "Crear cuenta" : "Iniciar sesión";
        SubmitButton.Text = _isRegistering ? "Registrarse" : "Entrar";
        NameFieldLayout.IsVisible = _isRegistering;
        PhoneFieldLayout.IsVisible = _isRegistering;
        EmergencyContactFieldLayout.IsVisible = _isRegistering;
        ToggleButton.Text = _isRegistering ? "Ya tengo cuenta" : "¿No tienes cuenta? Crea una";
    }

    private void OnTogglePasswordClicked(object? sender, EventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        PasswordEntry.IsPassword = !_isPasswordVisible;
        TogglePasswordButton.Text = _isPasswordVisible ? "Ocultar" : "Mostrar";
    }

    private async void OnForgotPasswordTapped(object? sender, TappedEventArgs e)
    {
        // TODO: conectar con el flujo real de recuperacion de contraseña.
        await DisplayAlertAsync("Próximamente", "La recuperación de contraseña estará disponible pronto.", "OK");
    }

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim();
        var password = PasswordEntry.Text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            await DisplayAlertAsync("Error", "Correo y contraseña son obligatorios.", "OK");
            return;
        }

        SubmitButton.IsEnabled = false;
        try
        {
            AuthResponse? auth;
            if (_isRegistering)
            {
                var name = NameEntry.Text?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    await DisplayAlertAsync("Error", "Indica tu nombre.", "OK");
                    return;
                }

                auth = await _api.RegisterAsync(new RegisterRequest
                {
                    Name = name,
                    Email = email,
                    Password = password,
                    Phone = PhoneEntry.Text?.Trim(),
                    EmergencyContact = EmergencyContactEntry.Text?.Trim()
                });
            }
            else
            {
                auth = await _api.LoginAsync(new LoginRequest { Email = email, Password = password });
            }

            if (auth is null)
            {
                await DisplayAlertAsync(_isRegistering ? "Registro fallido" : "Credenciales inválidas",
                    _isRegistering ? "No se pudo crear la cuenta. Verifica tus datos." : "Correo o contraseña incorrectos.",
                    "OK");
                return;
            }

            _session.Start(auth);
            _api.SetAuthToken(auth.Token);
            await SaveRememberedAsync();
            App.GoToMainShell();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }

    private async void OnFacebookClicked(object? sender, EventArgs e)
        => await OpenSocialAsync("https://www.facebook.com/proximaparadat");

    private async void OnInstagramClicked(object? sender, EventArgs e)
        => await OpenSocialAsync("https://www.instagram.com/proxima_parada_tours/");

    private async Task OpenSocialAsync(string url)
    {
        try
        {
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception)
        {
            await DisplayAlertAsync("No se pudo abrir", url, "OK");
        }
    }
}