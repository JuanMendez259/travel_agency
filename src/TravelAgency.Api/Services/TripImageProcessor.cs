using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace TravelAgency.Api.Services;

/// <summary>
/// Valida y prepara una imagen antes de subirla al storage.
/// Politica:
///  - Tope duro de entrada (20 MB): por encima se rechaza.
///  - Imagener <= 5 MB y sin exceder 1920 px de lado mayor: se sube tal cual.
///  - Si pesa mas o es mas grande, se redimensiona a 1920 px y se re-encodea
///    (JPG calidad 80, PNG o WebP segun el formato original).
///  - Si aun asi supera 5 MB tras comprimir, se rechaza.
/// </summary>
public static class TripImageProcessor
{
    public const long MaxInputBytes = 20L * 1024 * 1024;
    public const int MaxDimension = 1920;
    public const long TargetMaxBytes = 5L * 1024 * 1024;

    public static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    /// <summary>
    /// Devuelve el stream listo para subir (posicion 0). Cuando no hizo falta
    /// comprimir devuelve el mismo <paramref name="input"/> con Owned=false; si
    /// comprimio devuelve un MemoryStream nuevo con Owned=true para que el
    /// llamador lo disponga.
    /// </summary>
    public static (Stream Stream, string ContentType, bool Owned, bool Compressed) Prepare(
        MemoryStream input, string extension)
    {
        input.Position = 0;

        ImageInfo info;
        try
        {
            info = Image.Identify(input);
            var needsResize = info.Width > MaxDimension || info.Height > MaxDimension;
            var needsCompression = input.Length > TargetMaxBytes || needsResize;

            if (!needsCompression)
            {
                // Image.Identify avanza la posicion del stream; hay que regresarla a 0
                // o el upload subiria el archivo truncado (sin cabecera).
                input.Position = 0;
                return (input, ContentTypeFor(extension), Owned: false, Compressed: false);
            }

            input.Position = 0;
            using var image = Image.Load(input);

            if (needsResize)
            {
                var scale = Math.Min((float)MaxDimension / info.Width, (float)MaxDimension / info.Height);
                if (scale < 1f)
                {
                    var newWidth = (int)Math.Max(1, Math.Round(info.Width * scale));
                    var newHeight = (int)Math.Max(1, Math.Round(info.Height * scale));
                    image.Mutate(x => x.Resize(newWidth, newHeight));
                }
            }

            var output = new MemoryStream();
            switch (extension.ToLowerInvariant())
            {
                case ".webp":
                    image.Save(output, new WebpEncoder());
                    break;
                case ".png":
                    image.Save(output, new PngEncoder());
                    break;
                default:
                    image.Save(output, new JpegEncoder { Quality = 80 });
                    break;
            }

            if (output.Length > TargetMaxBytes)
            {
                output.Dispose();
                throw new InvalidOperationException(
                    "La imagen sigue superando los 5 MB incluso después de comprimirla. " +
                    "Prueba con una imagen más liviana.");
            }

            output.Position = 0;
            return (output, ContentTypeFor(extension), Owned: true, Compressed: true);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("El archivo no es una imagen válida.");
        }
    }
}