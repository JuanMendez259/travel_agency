using Android.Content;
using Android.Provider;

namespace TravelAgency.App.Services;

/// Guarda un PNG en la galería del dispositivo (Android).
public static class PhotoSaverService
{
    public static Task SavePngAsync(byte[] pngBytes, string fileName)
    {
        var resolver = Application.Context.ContentResolver!;
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "image/png");
        values.Put(MediaStore.IMediaColumns.RelativePath!, "Pictures/");
        values.Put(MediaStore.IMediaColumns.IsPending!, 1);

        var uri = resolver.Insert(MediaStore.Images.Media.ExternalContentUri!, values)!;
        try
        {
            using var output = resolver.OpenOutputStream(uri)!;
            output.Write(pngBytes, 0, pngBytes.Length);
            values.Clear();
            values.Put(MediaStore.IMediaColumns.IsPending!, 0);
            resolver.Update(uri, values, null, null);
            return Task.CompletedTask;
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
    }
}