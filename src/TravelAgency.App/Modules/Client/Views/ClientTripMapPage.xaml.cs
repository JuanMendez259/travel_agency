using Mapsui;
using Mapsui.Layers;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;
using Map = Mapsui.Map;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(TripId), "tripId")]
public partial class ClientTripMapPage : ContentPage
{
    private readonly ApiService _api;
    private bool _layersReady;
    private bool _loaded;
    private Trip? _trip;
    private List<MapMarkerInfo> _markers = new();

    public string TripId { get; set; } = string.Empty;

    public ClientTripMapPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
        TripMap.Map ??= new Map();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;

        try
        {
            await LoadMapAsync();
            _loaded = true;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar el mapa: {ex.Message}", "OK");
        }
    }

    private async Task LoadMapAsync()
    {
        if (!int.TryParse(TripId, out var id)) return;

        var trips = await _api.GetTripsAsync();
        _trip = trips?.FirstOrDefault(t => t.Id == id);
        if (_trip is null)
        {
            _trip = await _api.GetTripAsync(id);
        }

        if (_trip is null)
        {
            await DisplayAlertAsync("Error", "No se encontró el viaje.", "OK");
            return;
        }

        TripLabel.Text = _trip.Title;

        if (!_layersReady)
        {
            _layersReady = true;
            TripMapRenderer.AddTileLayer(TripMap.Map);
        }

        _markers = BuildMarkers();
        RefreshMapLayers();
        TripMapRenderer.FitToMarkers(TripMap.Map, _markers);
        RenderItinerary(_trip);
    }

    private List<MapMarkerInfo> BuildMarkers()
    {
        var list = new List<MapMarkerInfo>();
        if (_trip is null) return list;

        if (_trip.OriginLatitude is double olat && _trip.OriginLongitude is double olng)
            list.Add(new MapMarkerInfo(MapMarkerKinds.Origin, 0, olat, olng));

        foreach (var poi in _trip.PointsOfInterest?.OrderBy(p => p.Order) ?? Enumerable.Empty<TripPointOfInterest>())
            list.Add(new MapMarkerInfo(MapMarkerKinds.Poi, poi.Id, poi.Latitude, poi.Longitude));

        if (_trip.DestinationLatitude is double dlat && _trip.DestinationLongitude is double dlng)
            list.Add(new MapMarkerInfo(MapMarkerKinds.Dest, 0, dlat, dlng));

        return list;
    }

    private void RefreshMapLayers()
    {
        if (TripMap.Map is not { } map) return;
        RemoveLayer(map, "ruta");
        RemoveLayer(map, "marcadores");
        map.Layers.Add(TripMapRenderer.BuildRouteLayer(_markers), 1);
        map.Layers.Add(TripMapRenderer.BuildMarkersLayer(_markers), 2);
        map.Refresh(ChangeType.Discrete);
    }

    private static void RemoveLayer(Map map, string name)
        => map.Layers.Remove(layer => layer.Name == name);

    private void RenderItinerary(Trip trip)
    {
        var stops = new List<(string Title, string Detail, bool IsEndpoint)>();

        if (trip.OriginLatitude is not null && trip.OriginLongitude is not null)
            stops.Add(("Punto de partida", "Marcado por la agencia en el mapa", false));

        foreach (var poi in trip.PointsOfInterest?.OrderBy(p => p.Order) ?? Enumerable.Empty<TripPointOfInterest>())
        {
            var detail = string.IsNullOrWhiteSpace(poi.Description) ? "—" : poi.Description!;
            stops.Add((string.IsNullOrWhiteSpace(poi.Name) ? $"Punto {poi.Order}" : poi.Name!, detail, false));
        }

        if (trip.DestinationLatitude is not null && trip.DestinationLongitude is not null)
            stops.Add(("Punto de llegada", trip.Destination, true));

        ItineraryLayout.Clear();

        if (stops.Count == 0)
        {
            ItineraryLayout.Add(new Label
            {
                Text = "Este viaje todavía no tiene ruta publicada.",
                FontSize = 13,
                TextColor = Color.FromArgb("#404941")
            });
            return;
        }

        for (var i = 0; i < stops.Count; i++)
        {
            var (title, detail, isEndpoint) = stops[i];

            var markerColor = isEndpoint
                ? Color.FromArgb("#006C49")
                : i == 0
                    ? Color.FromArgb("#003B1B")
                    : Color.FromArgb("#006C49");

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(26, GridUnitType.Absolute)));
            row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            row.Add(new Border
            {
                WidthRequest = 22,
                HeightRequest = 22,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(11) },
                BackgroundColor = markerColor,
                VerticalOptions = LayoutOptions.Start,
                Content = new Label
                {
                    Text = (i + 1).ToString(),
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            }, 0, 0);

            var texts = new VerticalStackLayout { Spacing = 1 };
            texts.Add(new Label
            {
                Text = title,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#131B2E")
            });
            texts.Add(new Label
            {
                Text = detail,
                FontSize = 12,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 2,
                TextColor = Color.FromArgb("#404941")
            });
            row.Add(texts, 1, 0);

            ItineraryLayout.Add(row);
        }
    }
}