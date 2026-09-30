using Mapsui;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;
using Map = Mapsui.Map;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminTripMapPage : ContentPage
{
    private readonly ApiService _api;
    private readonly NominatimGeocoder _geocoder;

    private Trip? _trip;
    private string _mode = "pan";
    private bool _layersReady;
    private List<MapMarkerInfo> _markers = new();

    public string TripId { get; set; } = string.Empty;

    public AdminTripMapPage(ApiService api, NominatimGeocoder geocoder)
    {
        InitializeComponent();
        _api = api;
        _geocoder = geocoder;
        TripMap.Map ??= new Map();
        TripMap.Map.Tapped += OnMapTapped;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadMapAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar el mapa: {ex.Message}", "OK");
        }
    }

    private async Task LoadMapAsync()
    {
        if (!int.TryParse(TripId, out var id)) return;

        var trips = await _api.GetAdminTripsAsync();
        _trip = trips?.FirstOrDefault(t => t.Id == id);
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
        UpdateHint();
    }

    private List<MapMarkerInfo> BuildMarkers()
    {
        var list = new List<MapMarkerInfo>();
        if (_trip is null) return list;

        if (_trip.OriginLatitude is double olat && _trip.OriginLongitude is double olng)
            list.Add(new MapMarkerInfo(MapMarkerKinds.Origin, 0, olat, olng));

        foreach (var poi in _trip.PointsOfInterest.OrderBy(p => p.Order))
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

    private async void OnMapTapped(object? sender, MapEventArgs e)
    {
        if (_trip is null || TripMap.Map is not { } map) return;

        try
        {
            var world = e.WorldPosition;
            if (world is null) return;

            var (lng, lat) = TripMapRenderer.ProjectToLonLat(world.X, world.Y);
            var resolution = map.Navigator.Viewport.Resolution;

            switch (_mode)
            {
                case MapMarkerKinds.Origin:
                    await SaveOriginAsync(lat, lng);
                    SetMode("pan");
                    break;
                case MapMarkerKinds.Dest:
                    await SaveDestAsync(lat, lng);
                    SetMode("pan");
                    break;
                case MapMarkerKinds.Poi:
                    await AddPoiAtAsync(lat, lng, null);
                    SetMode("pan");
                    break;
                default:
                    var hit = TripMapRenderer.HitTest(_markers, world, resolution);
                    if (hit is not null) await HandleMarkerTapAsync(hit);
                    break;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async Task SaveOriginAsync(double lat, double lng)
    {
        _trip!.OriginLatitude = lat;
        _trip.OriginLongitude = lng;
        await _api.UpdateTripRouteAsync(_trip.Id, lat, lng, _trip.DestinationLatitude, _trip.DestinationLongitude);
        _markers = BuildMarkers();
        RefreshMapLayers();
    }

    private async Task SaveDestAsync(double lat, double lng)
    {
        _trip!.DestinationLatitude = lat;
        _trip.DestinationLongitude = lng;
        await _api.UpdateTripRouteAsync(_trip.Id, _trip.OriginLatitude, _trip.OriginLongitude, lat, lng);
        _markers = BuildMarkers();
        RefreshMapLayers();
    }

    private async Task AddPoiAtAsync(double lat, double lng, string? defaultName)
    {
        var name = await DisplayPromptAsync("Punto de interés", "Nombre del punto:", accept: "OK", cancel: "Cancelar",
            initialValue: defaultName ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;

        var description = await DisplayPromptAsync("Punto de interés", "Descripción (opcional):", accept: "OK", cancel: "Cancelar");

        var poi = await _api.AddPoiAsync(_trip!.Id, new TripPointOfInterest
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Latitude = lat,
            Longitude = lng
        });

        if (poi is not null) _trip.PointsOfInterest.Add(poi);

        _markers = BuildMarkers();
        RefreshMapLayers();
    }

    private async Task HandleMarkerTapAsync(MapMarkerInfo marker)
    {
        switch (marker.Kind)
        {
            case MapMarkerKinds.Origin:
            {
                var option = await DisplayActionSheetAsync("Origen", "Cancelar", null, "Fijar de nuevo", "Quitar origen");
                if (option == "Fijar de nuevo")
                    SetMode(MapMarkerKinds.Origin);
                else if (option == "Quitar origen")
                    await ClearOriginAsync();
                break;
            }
            case MapMarkerKinds.Dest:
            {
                var option = await DisplayActionSheetAsync("Destino", "Cancelar", null, "Fijar de nuevo", "Quitar destino");
                if (option == "Fijar de nuevo")
                    SetMode(MapMarkerKinds.Dest);
                else if (option == "Quitar destino")
                    await ClearDestAsync();
                break;
            }
            case MapMarkerKinds.Poi:
            {
                var poi = _trip?.PointsOfInterest.FirstOrDefault(p => p.Id == marker.PoiId);
                var option = await DisplayActionSheetAsync(poi?.Name ?? "Punto de interés", "Cancelar", null, "Editar", "Eliminar");
                if (option == "Editar" && poi is not null)
                    await EditPoiAsync(poi);
                else if (option == "Eliminar" && poi is not null)
                    await DeletePoiAsync(poi);
                break;
            }
        }
    }

    private async Task ClearOriginAsync()
    {
        _trip!.OriginLatitude = null;
        _trip.OriginLongitude = null;
        await _api.UpdateTripRouteAsync(_trip.Id, null, null, _trip.DestinationLatitude, _trip.DestinationLongitude);
        _markers = BuildMarkers();
        RefreshMapLayers();
    }

    private async Task ClearDestAsync()
    {
        _trip!.DestinationLatitude = null;
        _trip.DestinationLongitude = null;
        await _api.UpdateTripRouteAsync(_trip.Id, _trip.OriginLatitude, _trip.OriginLongitude, null, null);
        _markers = BuildMarkers();
        RefreshMapLayers();
    }

    private async Task EditPoiAsync(TripPointOfInterest poi)
    {
        var marker = _markers.FirstOrDefault(m => m.Kind == MapMarkerKinds.Poi && m.PoiId == poi.Id);
        var lat = marker?.Latitude ?? poi.Latitude;
        var lng = marker?.Longitude ?? poi.Longitude;

        var name = await DisplayPromptAsync("Editar punto", "Nombre:", accept: "OK", cancel: "Cancelar",
            initialValue: poi.Name ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;

        var description = await DisplayPromptAsync("Editar punto", "Descripción (opcional):", accept: "OK", cancel: "Cancelar",
            initialValue: poi.Description ?? "");

        var updated = await _api.UpdatePoiAsync(_trip!.Id, poi.Id, new TripPointOfInterest
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Latitude = lat,
            Longitude = lng
        });

        if (updated is not null)
        {
            poi.Name = updated.Name;
            poi.Description = updated.Description;
        }

        _markers = BuildMarkers();
        RefreshMapLayers();
    }

    private async Task DeletePoiAsync(TripPointOfInterest poi)
    {
        var confirm = await DisplayAlertAsync("Eliminar punto", $"¿Eliminar \"{poi.Name}\"?", "Sí", "No");
        if (!confirm) return;

        await _api.DeletePoiAsync(_trip!.Id, poi.Id);
        _trip.PointsOfInterest.Remove(poi);
        _markers = BuildMarkers();
        RefreshMapLayers();
    }

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        var query = PlaceSearch.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query) || SearchButton.IsEnabled == false) return;

        SearchButton.IsEnabled = false;
        try
        {
            var results = await _geocoder.SearchAsync(query);
            if (results.Count == 0)
            {
                await DisplayAlertAsync("Sin resultados", "No se encontró ningún lugar con ese texto.", "OK");
                return;
            }

            var selected = await DisplayActionSheetAsync(
                "Resultados", "Cancelar", null,
                results.Select(r => r.DisplayName).ToArray());

            if (string.IsNullOrEmpty(selected) || selected == "Cancelar") return;

            var result = results.FirstOrDefault(r => r.DisplayName == selected);
            if (result is null) return;

            switch (_mode)
            {
                case MapMarkerKinds.Origin:
                    await SaveOriginAsync(result.Latitude, result.Longitude);
                    SetMode("pan");
                    break;
                case MapMarkerKinds.Dest:
                    await SaveDestAsync(result.Latitude, result.Longitude);
                    SetMode("pan");
                    break;
                case MapMarkerKinds.Poi:
                    await AddPoiAtAsync(result.Latitude, result.Longitude, result.Name);
                    SetMode("pan");
                    break;
                default:
                    var (mx, my) = TripMapRenderer.ProjectToMercator(result.Longitude, result.Latitude);
                    TripMap.Map?.Navigator.CenterOnAndZoomTo(new MPoint(mx, my), 50);
                    break;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void SetMode(string mode)
    {
        _mode = mode;
        UpdateHint();
    }

    private void UpdateHint() => ModeHint.Text = _mode switch
    {
        MapMarkerKinds.Origin => "Toca el mapa para fijar el ORIGEN, o busca un lugar arriba.",
        MapMarkerKinds.Dest => "Toca el mapa para fijar el DESTINO, o busca un lugar arriba.",
        MapMarkerKinds.Poi => "Toca el mapa para agregar el PUNTO DE INTERÉS, o busca un lugar arriba.",
        _ => "Busca un lugar o usa los botones para marcar origen, puntos y destino. Toca un marcador para editarlo."
    };

    private void OnOriginModeClicked(object? sender, EventArgs e) => SetMode(MapMarkerKinds.Origin);

    private void OnPoiModeClicked(object? sender, EventArgs e) => SetMode(MapMarkerKinds.Poi);

    private void OnDestModeClicked(object? sender, EventArgs e) => SetMode(MapMarkerKinds.Dest);

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}