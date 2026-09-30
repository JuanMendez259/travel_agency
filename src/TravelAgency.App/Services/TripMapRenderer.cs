using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using Color = Mapsui.Styles.Color;
using Map = Mapsui.Map;
using Brush = Mapsui.Styles.Brush;
using MapsuiFont = Mapsui.Styles.Font;

namespace TravelAgency.App.Services;

public static class MapMarkerKinds
{
    public const string Origin = "origin";
    public const string Dest = "dest";
    public const string Poi = "poi";
}

/// Ubicación de un punto dibujado en el mapa (origen, destino o POI).
public sealed record MapMarkerInfo(string Kind, int PoiId, double Latitude, double Longitude);

/// Construye las capas del mapa (OpenStreetMap + marcadores + ruta) a partir
/// de la información del viaje. Es usado tanto por el editor del admin como
/// por el detalle de reserva del cliente (solo lectura).
public static class TripMapRenderer
{
    /// Escala de Web Mercator (EPSG:3857): metros por grado de longitud.
    private const double WebMercatorScale = 20037508.342789244 / 180.0;
    /// Radio de la Tierra usado por EPSG:3857 (esferoide esférico).
    private const double EarthRadius = 6378137.0;

    private static readonly Color OriginColor = Color.FromArgb(255, 0, 200, 83);
    private static readonly Color DestColor = Color.FromArgb(255, 211, 47, 47);
    private static readonly Color PoiColor = Color.FromArgb(255, 30, 136, 229);
    private static readonly Color RouteColor = Color.FromArgb(255, 21, 101, 192);
    private static readonly Color StrokeColor = Color.White;

    /// Convierte coordenadas geográficas (WGS84 lon/lat) a Web Mercator (EPSG:3857),
    /// que es el sistema en el que Mapsui dibuja los tiles de OpenStreetMap.
    /// Las MemoryLayer de Mapsui NO reproyectan solas: hay que entregarles los
    /// puntos ya en EPSG:3857, o aparecen en el lugar equivocado (casi en el (0,0)).
    public static (double X, double Y) ProjectToMercator(double longitude, double latitude)
    {
        var x = longitude * WebMercatorScale;
        var radians = latitude * Math.PI / 180.0;
        var y = Math.Log(Math.Tan(Math.PI / 4.0 + radians / 2.0)) * EarthRadius;
        return (x, y);
    }

    public static (double Longitude, double Latitude) ProjectToLonLat(double x, double y)
    {
        var longitude = x / WebMercatorScale;
        var radians = 2.0 * Math.Atan(Math.Exp(y / EarthRadius)) - Math.PI / 2.0;
        var latitude = radians * 180.0 / Math.PI;
        return (longitude, latitude);
    }

    private static Color ColorFor(string kind) => kind switch
    {
        MapMarkerKinds.Origin => OriginColor,
        MapMarkerKinds.Dest => DestColor,
        _ => PoiColor
    };

    public static void AddTileLayer(Map map)
    {
        map.Layers.Add(Mapsui.Tiling.OpenStreetMap.CreateTileLayer(), 0);
    }

    public static MemoryLayer BuildMarkersLayer(IReadOnlyList<MapMarkerInfo> markers)
    {
        var features = new List<IFeature>();
        var poiNumber = 0;

        // Orden estable: origen, poi por orden de aparicion, destino.
        foreach (var marker in markers)
        {
            var label = marker.Kind switch
            {
                MapMarkerKinds.Origin => "O",
                MapMarkerKinds.Dest => "D",
                _ => (++poiNumber).ToString()
            };

            var (px, py) = ProjectToMercator(marker.Longitude, marker.Latitude);
            var point = new PointFeature(new MPoint(px, py));
            point.Styles.Add(new SymbolStyle
            {
                Fill = new Brush(ColorFor(marker.Kind)),
                Outline = new Pen(StrokeColor, 2),
                SymbolType = SymbolType.Ellipse,
                UnitType = UnitType.Pixel,
                SymbolScale = 0.8
            });
            point.Styles.Add(new LabelStyle
            {
                Text = label,
                ForeColor = Color.White,
                Font = new MapsuiFont { Size = 13, Bold = true },
                Halo = new Pen(Color.Transparent)
            });
            features.Add(point);
        }

        return new MemoryLayer("marcadores")
        {
            Features = features
        };
    }

    public static MemoryLayer BuildRouteLayer(IReadOnlyList<MapMarkerInfo> ordered)
    {
        if (ordered.Count < 2)
            return new MemoryLayer("ruta") { Features = Array.Empty<IFeature>() };

        var coordinates = ordered
            .Select(m => ProjectToMercator(m.Longitude, m.Latitude))
            .Select(c => new Coordinate(c.X, c.Y))
            .ToArray();

        var line = new LineString(coordinates);
        var feature = new GeometryFeature(line);
        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(RouteColor, 4)
        });

        return new MemoryLayer("ruta")
        {
            Features = new IFeature[] { feature }
        };
    }

    /// Devuelve el marcador más cercano al toque si queda dentro del umbral
    /// (en píxeles), usando la resolución actual del viewport.
    /// El toque llega en coordenadas del mapa (EPSG:3857) y los marcadores se
    /// proyectan al mismo sistema para comparar distancias en metros.
    public static MapMarkerInfo? HitTest(IReadOnlyList<MapMarkerInfo> markers, MPoint worldPosition, double resolution, double tolerancePixels = 24)
    {
        if (markers.Count == 0 || worldPosition is null || resolution <= 0) return null;

        var tolerance = resolution * tolerancePixels;
        MapMarkerInfo? best = null;
        double bestDistance = tolerance;

        foreach (var marker in markers)
        {
            var (mx, my) = ProjectToMercator(marker.Longitude, marker.Latitude);
            var dx = mx - worldPosition.X;
            var dy = my - worldPosition.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = marker;
            }
        }

        return best;
    }

    /// Encaja la vista al conjunto de puntos (o centra en un punto suelto).
    public static void FitToMarkers(Map map, IReadOnlyList<MapMarkerInfo> markers)
    {
        if (markers.Count == 0)
        {
            var (mx, my) = ProjectToMercator(-99.1332, 19.4326); // Ciudad de México
            map.Navigator.CenterOnAndZoomTo(new MPoint(mx, my), 2000);
            return;
        }

        var projected = markers
            .Select(m => ProjectToMercator(m.Longitude, m.Latitude))
            .ToList();

        var minX = projected.Min(p => p.X);
        var maxX = projected.Max(p => p.X);
        var minY = projected.Min(p => p.Y);
        var maxY = projected.Max(p => p.Y);

        var width = Math.Max(maxX - minX, 100);
        var height = Math.Max(maxY - minY, 100);

        if (markers.Count == 1)
        {
            map.Navigator.CenterOnAndZoomTo(new MPoint(minX, minY), 50);
            return;
        }

        // Panotorno del 15% para que los marcadores no queden pegados al borde.
        map.Navigator.ZoomToBox(
            new MRect(minX - width * 0.15, minY - height * 0.15, maxX + width * 0.15, maxY + height * 0.15),
            MBoxFit.Fit);
    }
}