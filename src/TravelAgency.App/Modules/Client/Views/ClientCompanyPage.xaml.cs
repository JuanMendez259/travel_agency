using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientCompanyPage : ContentPage
{
    public ClientCompanyPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        VersionLabel.Text = $"v{AppInfo.Version} (Build {AppInfo.BuildString})";
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "RutaVerde México",
                Text = "Descubre RutaVerde: expediciones responsables en grupos reducidos, " +
                       "guiadas por biólogos y comunidades locales. Reserva en la app."
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo compartir", ex.Message, "OK");
        }
    }

    private async void OnPledgeClicked(object? sender, EventArgs e)
    {
        PledgeCard.TranslationY = 220;
        PledgeOverlay.IsVisible = true;
        await PledgeCard.TranslateTo(0, 0, 220, Easing.CubicOut);
    }

    private async void OnClosePledgeClicked(object? sender, EventArgs e)
    {
        await PledgeCard.TranslateTo(0, 220, 180, Easing.CubicIn);
        PledgeOverlay.IsVisible = false;
    }

    private async void OnPledgeBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (!PledgeOverlay.IsVisible) return;
        await PledgeCard.TranslateTo(0, 220, 180, Easing.CubicIn);
        PledgeOverlay.IsVisible = false;
    }

    private async void OnEmailTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync("mailto:hola@rutaverde.travel");
        }
        catch (Exception)
        {
            await DisplayAlertAsync("Contacto", "Escríbenos a hola@rutaverde.travel", "OK");
        }
    }

    private async void OnExploreClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//home");
    }
}
