namespace TravelAgency.Shared.Models;

public static class TripImageFileRules
{
    public static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp",
        };

    public const long MaxInputBytes = 20L * 1024 * 1024;

    public static bool IsAllowed(string? fileName, long length)
    {
        if (string.IsNullOrWhiteSpace(fileName) || length <= 0 || length > MaxInputBytes)
            return false;

        var extension = Path.GetExtension(fileName);
        return AllowedExtensions.Contains(extension);
    }
}
