using Microsoft.Maui.Controls;
using TravelAgency.App.Services;

namespace TravelAgency.App.Modules.Client.Views;

/// Visor de una sola foto: pellizco para zoom (1x-4x), arrastre cuando esta
/// ampliada y doble toque para acercar o restablecer.
public partial class ClientImageViewerPage : ContentPage, IQueryAttributable
{
    private const double MinScale = 1;
    private const double MaxScale = 4;
    private const double DoubleTapScale = 2.5;

    private readonly ApiService _api;
    private string? _url;
    private string _title = "Foto del viaje";
    private double _startScale = MinScale;
    private double _panStartX;
    private double _panStartY;

    public ClientImageViewerPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("url", out var url))
        {
            var raw = url?.ToString();
            // Shell ya entrega los parametros decodificados; solo decodificamos
            // si llego una URL que no se puede interpretar como absoluta.
            _url = string.IsNullOrWhiteSpace(raw)
                ? null
                : Uri.TryCreate(raw, UriKind.Absolute, out _)
                    ? raw
                    : Uri.UnescapeDataString(raw);
        }

        if (query.TryGetValue("title", out var title))
        {
            var raw = title?.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
                _title = raw!;
        }

        TitleLabel.Text = _title;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ResetTransform();
        ZoomImage.Source = _api.GetTripImage(_url, ApiService.FullImageWidth);
    }

    protected override void OnDisappearing()
    {
        ResetTransform();
        base.OnDisappearing();
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _startScale = ZoomImage.Scale;
                break;

            case GestureStatus.Running:
                ZoomImage.Scale = Math.Clamp(_startScale * e.Scale, MinScale, MaxScale);
                ClampTranslation();
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _startScale = ZoomImage.Scale;
                if (ZoomImage.Scale <= MinScale)
                    ResetTransform();
                else
                    ClampTranslation();
                break;
        }
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (ZoomImage.Scale <= MinScale)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = ZoomImage.TranslationX;
                _panStartY = ZoomImage.TranslationY;
                break;

            case GestureStatus.Running:
                ZoomImage.TranslationX = _panStartX + e.TotalX;
                ZoomImage.TranslationY = _panStartY + e.TotalY;
                ClampTranslation();
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                ClampTranslation();
                break;
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ZoomImage.Scale > MinScale)
        {
            ResetTransform();
            return;
        }

        ZoomImage.Scale = DoubleTapScale;
        ClampTranslation();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private void ClampTranslation()
    {
        var maxX = Math.Max(0, (ZoomImage.Scale - 1) * ZoomImage.Width / 2);
        var maxY = Math.Max(0, (ZoomImage.Scale - 1) * ZoomImage.Height / 2);

        ZoomImage.TranslationX = Math.Clamp(ZoomImage.TranslationX, -maxX, maxX);
        ZoomImage.TranslationY = Math.Clamp(ZoomImage.TranslationY, -maxY, maxY);
    }

    private void ResetTransform()
    {
        ZoomImage.Scale = MinScale;
        ZoomImage.TranslationX = 0;
        ZoomImage.TranslationY = 0;
        _startScale = MinScale;
        _panStartX = 0;
        _panStartY = 0;
    }
}
