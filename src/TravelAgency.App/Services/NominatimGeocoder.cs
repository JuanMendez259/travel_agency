using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TravelAgency.App.Services;

public sealed class GeocodeResult
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

/// Servicio de búsqueda de lugares (geocoding directo) usando Nominatim de
/// OpenStreetMap. Sustituye al autocompletado de Google Places.
/// Por política de uso de Nominatim se envía un User-Agent identificable y se
/// limita el historial; el dispositivo hace una petición por búsqueda.
public sealed class NominatimGeocoder
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("TravelAgencyApp", "1.0"));
        client.DefaultRequestHeaders.Add("Accept-Language", "es");
        return client;
    }

    public async Task<IReadOnlyList<GeocodeResult>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<GeocodeResult>();

        // OSM no tiene el número de casa / colonia en muchos lugares de México.
        // Si la búsqueda completa no da resultados, se re-intenta con variantes
        // progresivamente más generales en lugar de devolver "sin resultados".
        var attempts = BuildAttempts(query.Trim());
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attempt in attempts)
        {
            var results = await SearchOnceAsync(attempt);
            if (results.Count == 0) continue;

            var fresh = results.Where(r => seen.Add(r.DisplayName)).ToList();
            if (fresh.Count > 0) return fresh;
        }

        return new List<GeocodeResult>();
    }

    private static IReadOnlyList<string> BuildAttempts(string query)
    {
        var attempts = new List<string> { query };
        var parts = query.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return attempts;

        // 1) Quitar el número de casa del primer segmento, p.ej. "Calle X 4".
        var cleaned = Regex.Replace(parts[0], @"\s+\d{1,4}$", "");
        if (cleaned.Length > 0 && cleaned != parts[0])
        {
            var join = string.Join(", ", new[] { cleaned }.Concat(parts.Skip(1)));
            if (!attempts.Contains(join)) attempts.Add(join);
        }

        // 2) Quedarse con las últimas 2-3 partes (ciudad / código postal).
        for (var take = Math.Min(3, parts.Count); take >= 2; take--)
        {
            var join = string.Join(", ", parts.TakeLast(take));
            if (!attempts.Contains(join)) attempts.Add(join);
        }

        return attempts;
    }

    private async Task<IReadOnlyList<GeocodeResult>> SearchOnceAsync(string query)
    {
        var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=6&q=" +
            Uri.EscapeDataString(query);

        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var results = new List<GeocodeResult>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("lat", out var lat) ||
                !element.TryGetProperty("lon", out var lon)) continue;

            if (!double.TryParse(lat.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(lon.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)) continue;

            var name = element.TryGetProperty("display_name", out var dn) ? dn.GetString() : null;
            results.Add(new GeocodeResult
            {
                Name = string.IsNullOrWhiteSpace(name) ? query : name!,
                DisplayName = name ?? query,
                Latitude = latitude,
                Longitude = longitude
            });
        }

        return results;
    }
}