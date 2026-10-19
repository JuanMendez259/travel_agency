using TravelAgency.App.Services;

namespace TravelAgency.App.Modules.Auth.Views;

public partial class ProfileSetupPage : ContentPage
{
    private readonly ApiService _api;

    public ProfileSetupPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;

        ProfileNameLabel.Text = session.UserName ?? string.Empty;
        ProfileNameLabel.IsVisible = !string.IsNullOrWhiteSpace(ProfileNameLabel.Text);
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        var phone = PhoneNumberFormatter.Normalize(PhoneEntry.Text);
        if (phone is null)
        {
            await DisplayAlertAsync("Teléfono", "Indica un teléfono válido de 10 dígitos.", "OK");
            return;
        }

        ContinueButton.IsEnabled = false;
        try
        {
            var user = await _api.UpdateMyProfileAsync(phone, EmergencyContactEntry.Text?.Trim());
            if (user is null)
            {
                await DisplayAlertAsync("Perfil", "No pudimos guardar tu perfil. Intenta de nuevo.", "OK");
                return;
            }

            App.GoToMainShell();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Perfil", ex.Message, "OK");
        }
        finally
        {
            ContinueButton.IsEnabled = true;
        }
    }
}
