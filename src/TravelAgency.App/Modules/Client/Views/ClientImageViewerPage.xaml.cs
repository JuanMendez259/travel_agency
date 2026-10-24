using Microsoft.Maui.Controls;
using TravelAgency.App.Services;

namespace TravelAgency.App.Modules.Client.Views;

/// Visor de una sola foto: pinch con zoom al punto de apoyo, pan de uno o dos
/// dedos y doble toque para acercar o restablecer.
public partial class ClientImageViewerPage : ContentPage, IQueryAttributable
{
    private const double MinScale = 1;
    private const double MaxScale = 4;
    private const double DoubleTapScale = 2.5;

    private readonly ApiService _api;
    private string? _url;
    private string _title = "Foto del viaje";

    private double _startScale = MinScale;
    private double _startTranslationX;
    private double _startTranslationY;
    private double _pinchContentX;
    private double _pinchContentY;
    private bool _isPinching;

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

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width > 0 && height > 0)
            ResetTransform();
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (GetSurfaceWidth() <= 0 || GetSurfaceHeight() <= 0)
            return;

        switch (e.Status)
        {
            case GestureStatus.Started:
                _isPinching = true;
                _startScale = Math.Max(MinScale, ZoomImage.Scale);
                _startTranslationX = ZoomImage.TranslationX;
                _startTranslationY = ZoomImage.TranslationY;

                var start = GetPinchPoint(e.ScaleOrigin);
                var center = GetSurfaceCenter();
                _pinchContentX = (start.X - center.X - _startTranslationX) / _startScale;
                _pinchContentY = (start.Y - center.Y - _startTranslationY) / _startScale;
                break;

            case GestureStatus.Running:
                var current = GetPinchPoint(e.ScaleOrigin);
                var newScale = Math.Clamp(ZoomImage.Scale * e.Scale, MinScale, MaxScale);
                var surfaceCenter = GetSurfaceCenter();

                ZoomImage.Scale = newScale;
                ZoomImage.TranslationX = current.X - surfaceCenter.X - newScale * _pinchContentX;
                ZoomImage.TranslationY = current.Y - surfaceCenter.Y - newScale * _pinchContentY;
                ClampTranslation();
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _isPinching = false;
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
        if (_isPinching || ZoomImage.Scale <= MinScale)
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

        var point = e.GetPosition(GestureSurface) ?? GetSurfaceCenter();
        var center = GetSurfaceCenter();
        var contentX = (point.X - center.X - ZoomImage.TranslationX) / ZoomImage.Scale;
        var contentY = (point.Y - center.Y - ZoomImage.TranslationY) / ZoomImage.Scale;

        ZoomImage.Scale = DoubleTapScale;
        ZoomImage.TranslationX = point.X - center.X - DoubleTapScale * contentX;
        ZoomImage.TranslationY = point.Y - center.Y - DoubleTapScale * contentY;
        ClampTranslation();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private Point GetPinchPoint(Point origin)
        => new(origin.X * GetSurfaceWidth(), origin.Y * GetSurfaceHeight());

    private Point GetSurfaceCenter()
        => new(GetSurfaceWidth() / 2, GetSurfaceHeight() / 2);

    private double GetSurfaceWidth()
        => GestureSurface.Width > 0 ? GestureSurface.Width : Math.Max(1, Width);

    private double GetSurfaceHeight()
        => GestureSurface.Height > 0 ? GestureSurface.Height : Math.Max(1, Height);

    private void ClampTranslation()
    {
        var width = GetSurfaceWidth();
        var height = GetSurfaceHeight();
        var maxX = Math.Max(0, (ZoomImage.Scale - 1) * width / 2);
        var maxY = Math.Max(0, (ZoomImage.Scale - 1) * height / 2);

        ZoomImage.TranslationX = Math.Clamp(ZoomImage.TranslationX, -maxX, maxX);
        ZoomImage.TranslationY = Math.Clamp(ZoomImage.TranslationY, -maxY, maxY);
    }

    private void ResetTransform()
    {
        ZoomImage.Scale = MinScale;
        ZoomImage.TranslationX = 0;
        ZoomImage.TranslationY = 0;
        _startScale = MinScale;
        _startTranslationX = 0;
        _startTranslationY = 0;
        _pinchContentX = 0;
        _pinchContentY = 0;
        _isPinching = false;
        _panStartX = 0;
        _panStartY = 0;
    }
}
