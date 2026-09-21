using Microsoft.Maui.Controls;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientProfilePage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;

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

        PhoneLabel.Text = string.IsNullOrWhiteSpace(user.Phone) ? "No registrado" : user.Phone;

        QrImage.Source = QrCodeService.FromToken(user.QrToken);
        QrImage.IsVisible = QrImage.Source is not null;
    }
}