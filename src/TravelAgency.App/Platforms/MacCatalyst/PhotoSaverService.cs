using Foundation;
using Microsoft.Maui.ApplicationModel;
using UIKit;

namespace TravelAgency.App.Services;

/// Guarda un PNG en el carrete de fotos del dispositivo (Mac Catalyst).
public static class PhotoSaverService
{
    public static Task SavePngAsync(byte[] pngBytes, string fileName)
    {
        var image = UIImage.LoadFromData(NSData.FromArray(pngBytes));
        if (image is null)
            return Task.FromException(new InvalidOperationException("No se pudo generar la imagen del pase."));

        var tcs = new TaskCompletionSource();
        MainThread.BeginInvokeOnMainThread(() =>
            image.SaveToPhotosAlbum((img, error) =>
            {
                if (error is null)
                    tcs.TrySetResult();
                else
                    tcs.TrySetException(new Exception(error.LocalizedDescription));
            }));
        return tcs.Task;
    }
}