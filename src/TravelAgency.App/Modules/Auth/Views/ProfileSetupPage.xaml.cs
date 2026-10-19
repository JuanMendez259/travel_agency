using TravelAgency.App.Services;

namespace TravelAgency.App.Modules.Auth.Views;

public partial class ProfileSetupPage : ContentPage
{
    private readonly ApiService _api;

    public ProfileSetupPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        var phoneText = PhoneEntry.Text?.Trim() ?? string.Empty;
        var digits = new string(phoneText.Where(char.IsDigit).ToArray());
        string? normalizedPhone;
        if (digits.Length == 10)
        {
            normalizedPhone = $"52{digits}";
        }
        else if (digits.Length >= 11 && digits.Length <= 15)
        {
            normalizedPhone = digits;
        }
        else
        {
            await DisplayAlertAsync("Error", "Ingresa un número de teléfono válido de 10 dígitos.", "OK");
            return;
        }

        var emergencyContact = EmergencyContactEntry.Text?.Trim();

        var user = await _api.UpdateMyProfileAsync(normalizedPhone, emergencyContact);

        if (user is null)
        {
            await DisplayAlertAsync("Error", "No se pudo guardar el perfil. Intenta de nuevo.", "OK");
            return;
        }

        await Shell.Current.GoToAsync("//home");
    }
}