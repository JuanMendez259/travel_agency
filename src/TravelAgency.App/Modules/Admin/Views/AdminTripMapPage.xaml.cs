using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminTripMapPage : ContentPage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ApiService _api;
    private Trip? _trip;
    private bool _handlingNavigation;

    public string TripId { get; set; } = string.Empty;

    public AdminTripMapPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
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

        var cfg = new
        {
            originLat = _trip.OriginLatitude,
            originLng = _trip.OriginLongitude,
            destLat = _trip.DestinationLatitude,
            destLng = _trip.DestinationLongitude,
            pois = _trip.PointsOfInterest
                .OrderBy(p => p.Order)
                .Select(p => new { p.Id, p.Name, p.Description, lat = p.Latitude, lng = p.Longitude })
        };

        var cfgJson = JsonSerializer.Serialize(cfg, JsonOptions);

        var html = await ReadMapHtmlAsync();
        html = html
            .Replace("{{KEY}}", MapConfig.GoogleMapsApiKey)
            .Replace("{{CFG}}", "const CFG = " + cfgJson + ";");

        MapView.Source = new HtmlWebViewSource { Html = html };
    }

    private static async Task<string> ReadMapHtmlAsync()
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync("map.html");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private async void OnWebNavigating(object? sender, WebNavigatingEventArgs e)
    {
        var url = e.Url ?? "";
        if (!url.StartsWith("app://", StringComparison.OrdinalIgnoreCase)) return;

        e.Cancel = true;
        if (_handlingNavigation) return;
        _handlingNavigation = true;

        try
        {
            await HandleCommandAsync(url.Substring("app://".Length));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            _handlingNavigation = false;
        }
    }

    private async Task HandleCommandAsync(string raw)
    {
        var fragment = raw;
        var query = string.Empty;
        var qIndex = raw.IndexOf('?');
        if (qIndex >= 0)
        {
            fragment = raw.Substring(0, qIndex);
            query = raw.Substring(qIndex + 1);
        }

        var qs = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(a => a.Length == 2)
            .ToDictionary(
                a => a[0],
                a => Uri.UnescapeDataString(a[1].Replace("+", " ")),
                StringComparer.OrdinalIgnoreCase);

        double GetDouble(string key)
            => qs.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

        int GetInt(string key)
            => qs.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : 0;

        if (_trip is null) return;

        switch (fragment)
        {
            case "route":
            {
                var kind = qs.GetValueOrDefault("kind");
                var lat = GetDouble("lat");
                var lng = GetDouble("lng");

                if (kind == "origin")
                {
                    _trip.OriginLatitude = lat;
                    _trip.OriginLongitude = lng;
                    await _api.UpdateTripRouteAsync(_trip.Id, lat, lng, _trip.DestinationLatitude, _trip.DestinationLongitude);
                }
                else if (kind == "dest")
                {
                    _trip.DestinationLatitude = lat;
                    _trip.DestinationLongitude = lng;
                    await _api.UpdateTripRouteAsync(_trip.Id, _trip.OriginLatitude, _trip.OriginLongitude, lat, lng);
                }

                await SetModeAsync("pan");
                break;
            }

            case "pick":
            {
                var lat = GetDouble("lat");
                var lng = GetDouble("lng");

                var name = await DisplayPromptAsync("Punto de interés", "Nombre del punto:", accept: "OK", cancel: "Cancelar");
                if (string.IsNullOrWhiteSpace(name)) return;

                var description = await DisplayPromptAsync("Punto de interés", "Descripción (opcional):", accept: "OK", cancel: "Cancelar");

                var poi = await _api.AddPoiAsync(_trip.Id, new TripPointOfInterest
                {
                    Name = name.Trim(),
                    Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                    Latitude = lat,
                    Longitude = lng
                });

                if (poi is not null)
                {
                    _trip.PointsOfInterest.Add(poi);
                    await EvaluateJsSafeAsync($"addPoi({JsonSerializer.Serialize(new { poi.Id, poi.Name, poi.Description, lat = poi.Latitude, lng = poi.Longitude }, JsonOptions)});");
                }

                await SetModeAsync("pan");
                break;
            }

            case "edit-poi":
            {
                var id = GetInt("id");
                var lat = GetDouble("lat");
                var lng = GetDouble("lng");
                var existing = _trip.PointsOfInterest.FirstOrDefault(p => p.Id == id);
                if (existing is null) return;

                var name = await DisplayPromptAsync("Editar punto", "Nombre:", accept: "OK", cancel: "Cancelar", initialValue: existing.Name ?? "");
                if (string.IsNullOrWhiteSpace(name)) return;

                var description = await DisplayPromptAsync("Editar punto", "Descripción (opcional):", accept: "OK", cancel: "Cancelar", initialValue: existing.Description ?? "");

                var updated = await _api.UpdatePoiAsync(_trip.Id, id, new TripPointOfInterest
                {
                    Name = name.Trim(),
                    Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                    Latitude = lat,
                    Longitude = lng
                });

                if (updated is not null)
                {
                    existing.Name = updated.Name;
                    existing.Description = updated.Description;
                    await EvaluateJsSafeAsync($"updatePoi({id}, {JsonSerializer.Serialize(new { updated.Name, Description = updated.Description }, JsonOptions)});");
                }
                break;
            }

            case "poi-move":
            {
                var id = GetInt("id");
                var lat = GetDouble("lat");
                var lng = GetDouble("lng");
                var existing = _trip.PointsOfInterest.FirstOrDefault(p => p.Id == id);
                if (existing is null) return;

                await _api.UpdatePoiAsync(_trip.Id, id, new TripPointOfInterest
                {
                    Name = existing.Name,
                    Description = existing.Description,
                    Latitude = lat,
                    Longitude = lng
                });

                existing.Latitude = lat;
                existing.Longitude = lng;
                break;
            }

            case "del-poi":
            {
                var id = GetInt("id");
                var existing = _trip.PointsOfInterest.FirstOrDefault(p => p.Id == id);
                if (existing is null) return;

                var confirm = await DisplayAlertAsync("Eliminar punto", $"¿Eliminar \"{existing.Name}\"?", "Sí", "No");
                if (!confirm) return;

                await _api.DeletePoiAsync(_trip.Id, id);
                _trip.PointsOfInterest.Remove(existing);
                await EvaluateJsSafeAsync($"removePoi({id});");
                break;
            }
        }
    }

    private async Task SetModeAsync(string mode)
    {
        await EvaluateJsSafeAsync($"setMode('{mode}');");
    }

    private async Task EvaluateJsSafeAsync(string script)
    {
        try
        {
            await MapView.EvaluateJavaScriptAsync(script);
        }
        catch
        {
        }
    }

    private async void OnOriginModeClicked(object? sender, EventArgs e)
    {
        await SetModeAsync("origin");
    }

    private async void OnPoiModeClicked(object? sender, EventArgs e)
    {
        await SetModeAsync("poi");
    }

    private async void OnDestModeClicked(object? sender, EventArgs e)
    {
        await SetModeAsync("dest");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}