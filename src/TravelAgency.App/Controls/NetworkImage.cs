using Microsoft.Maui.Controls;
using TravelAgency.App.Services;

namespace TravelAgency.App.Controls;

/// Imagen de red con el placeholder detras: si la descarga falla (sin red, 404)
/// queda visible el placeholder en vez de un hueco vacio. La descarga y el
/// cacheo siguen siendo de la plataforma (UriImageSource).
public class NetworkImage : ContentView
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(ImageSource), typeof(NetworkImage), null,
        propertyChanged: (bindable, _, value) =>
            ((NetworkImage)bindable).Main.Source = (ImageSource?)value);

    public static readonly BindableProperty AspectProperty = BindableProperty.Create(
        nameof(Aspect), typeof(Aspect), typeof(NetworkImage), Aspect.AspectFit,
        propertyChanged: (bindable, _, value) => ((NetworkImage)bindable).ApplyAspect((Aspect)value));

    private readonly Image _placeholder = new() { Source = ApiService.PlaceholderSource() };
    private readonly Image _main = new();

    private Image Main => _main;

    public NetworkImage()
    {
        Content = new Grid { Children = { _placeholder, _main } };
        ApplyAspect(Aspect);
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Aspect Aspect
    {
        get => (Aspect)GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    private void ApplyAspect(Aspect aspect)
    {
        _placeholder.Aspect = aspect;
        _main.Aspect = aspect;
    }
}
