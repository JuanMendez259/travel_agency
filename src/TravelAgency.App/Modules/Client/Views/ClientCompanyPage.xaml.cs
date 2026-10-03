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
}
