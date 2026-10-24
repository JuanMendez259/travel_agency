using Microsoft.Maui.Controls;
using TravelAgency.App.Services;
using TravelAgency.App.ViewModels;

namespace TravelAgency.App.Controls;

/// Carrusel de fotos del viaje. Cuando solo hay una imagen (o ninguna) se
/// comporta como una foto fija: sin swipe, sin puntos y sin contador.
public partial class TripImageCarousel : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable<TripImageItem>), typeof(TripImageCarousel), null,
        propertyChanged: (bindable, _, value) =>
            ((TripImageCarousel)bindable).ApplyItems(value as IEnumerable<TripImageItem>));

    public static readonly BindableProperty CounterMarginProperty = BindableProperty.Create(
        nameof(CounterMargin), typeof(Thickness), typeof(TripImageCarousel), new Thickness(0, 12, 12, 0));

    private int _count;

    public event EventHandler<TripImageTappedEventArgs>? ImageTapped;

    public TripImageCarousel()
    {
        InitializeComponent();
    }

    public IEnumerable<TripImageItem>? ItemsSource
    {
        get => (IEnumerable<TripImageItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// Margen del contador (arriba a la derecha). Las vistas con botones
    /// superpuestos pueden bajarlo para no taparlos.
    public Thickness CounterMargin
    {
        get => (Thickness)GetValue(CounterMarginProperty);
        set => SetValue(CounterMarginProperty, value);
    }

    private void ApplyItems(IEnumerable<TripImageItem>? items)
    {
        var list = items?.ToList() ?? new List<TripImageItem>();
        if (list.Count == 0)
            list.Add(new TripImageItem(null, ApiService.PlaceholderSource(), isCover: true));

        _count = list.Count;
        Carousel.ItemsSource = list;
        Carousel.Position = 0;

        var multiple = _count > 1;
        Carousel.IsSwipeEnabled = multiple;
        IndicatorsHost.IsVisible = multiple;
        CounterLabel.IsVisible = multiple;
        UpdateCounter();
    }

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e) => UpdateCounter();

    private void UpdateCounter()
    {
        if (_count <= 0)
        {
            CounterLabel.Text = string.Empty;
            return;
        }

        var position = Math.Clamp(Carousel.Position, 0, _count - 1);
        CounterLabel.Text = $"{position + 1} / {_count}";
    }

    private void OnImageTapped(object? sender, TappedEventArgs e)
    {
        var item = e.Parameter as TripImageItem;
        ImageTapped?.Invoke(this, new TripImageTappedEventArgs(item?.Url, Math.Max(0, Carousel.Position)));
    }
}

public sealed class TripImageTappedEventArgs : EventArgs
{
    public string? Url { get; }
    public int Index { get; }

    public TripImageTappedEventArgs(string? url, int index)
    {
        Url = url;
        Index = index;
    }
}
