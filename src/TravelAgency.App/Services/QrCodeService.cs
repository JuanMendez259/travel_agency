using Microsoft.Maui.Controls;
using QRCoder;

namespace TravelAgency.App.Services;

public static class QrCodeService
{
    public static ImageSource? FromToken(string? token, int pixels = 260)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(token, QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(data);
            var bytes = png.GetGraphic(pixels);
            return ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }
}