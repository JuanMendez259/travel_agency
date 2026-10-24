using Microsoft.Maui.Controls;

namespace TravelAgency.App.ViewModels;

/// Una foto de la galeria del viaje ya resuelta a ImageSource para el carrusel.
public sealed class TripImageItem
{
    public string? Url { get; }
    public ImageSource Source { get; }
    public bool IsCover { get; }

    public TripImageItem(string? url, ImageSource source, bool isCover)
    {
        Url = url;
        Source = source;
        IsCover = isCover;
    }
}
