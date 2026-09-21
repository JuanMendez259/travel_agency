using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Auth.Views;

public partial class LoginPage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private bool _isRegistering;

    public LoginPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        QuickLoginPicker.ItemsSource = new[] { "Admin", "Cliente" };
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
        NameEntry.IsVisible = _isRegistering;
        PhoneEntry.IsVisible = _isRegistering;
        ToggleButton.Text = _isRegistering ? "Ya tengo cuenta" : "¿No tienes cuenta? Crea una";
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
                    Phone = PhoneEntry.Text?.Trim()
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
}