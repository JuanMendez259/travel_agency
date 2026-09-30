using Microsoft.Maui.Controls;
using QRCoder;

namespace TravelAgency.App.Services;

public static class QrCodeService
{
    public static ImageSource? FromToken(string? token, int pixels = 260)
    {
        var bytes = PngBytes(token, pixels);
        return bytes is null ? null : ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    public static byte[]? PngBytes(string? token, int pixels = 260)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(token, QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(data);
            return png.GetGraphic(pixels);
        }
        catch
        {
            return null;
        }
    }
}