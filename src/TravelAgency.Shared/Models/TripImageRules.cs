namespace TravelAgency.Shared.Models;

public static class TripImageRules
{
    public const int MaxPerTrip = 10;

    public static bool HasCapacity(int currentCount, int addCount)
    {
        if (addCount <= 0)
            return false;

        var current = Math.Max(0, currentCount);
        return current + addCount <= MaxPerTrip;
    }

    public static int NextOrder(IEnumerable<TripImage> images)
    {
        var list = images?.ToList() ?? new List<TripImage>();
        return list.Count == 0 ? 1 : list.Max(i => i.Order) + 1;
    }

    public static void NormalizeOrder(IList<TripImage> images)
    {
        if (images is null || images.Count == 0)
            return;

        var ordered = images
            .OrderBy(i => i.Order)
            .ThenBy(i => i.Id)
            .ToList();

        images.Clear();
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].Order = index + 1;
            images.Add(ordered[index]);
        }
    }

    public static TripImage? ChooseCover(IEnumerable<TripImage> images)
    {
        if (images is null)
            return null;

        var list = images.ToList();
        return list.FirstOrDefault(i => i.IsCover)
            ?? list.OrderBy(i => i.Order).ThenBy(i => i.Id).FirstOrDefault();
    }

    public static bool IsValidOrder(IEnumerable<int> requestedIds, IEnumerable<int> currentIds)
    {
        if (requestedIds is null || currentIds is null)
            return false;

        var requested = requestedIds.ToList();
        var current = currentIds.ToList();

        if (requested.Count != current.Count)
            return false;

        if (requested.Distinct().Count() != requested.Count)
            return false;

        return new HashSet<int>(requested).SetEquals(current);
    }

    public static IReadOnlyList<string> BuildGalleryUrls(Trip trip)
    {
        if (trip is null)
            return Array.Empty<string>();

        var urls = (trip.Images ?? new List<TripImage>())
            .OrderBy(i => i.Order)
            .ThenBy(i => i.Id)
            .Select(i => i.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url) && !IsPlaceholderUrl(url))
            .ToList();

        if (urls.Count > 0)
            return urls;

        return string.IsNullOrWhiteSpace(trip.ImageUrl) || IsPlaceholderUrl(trip.ImageUrl)
            ? Array.Empty<string>()
            : new[] { trip.ImageUrl! };
    }

    private static bool IsPlaceholderUrl(string? url)
        => url?.EndsWith("/placeholder.jpg", StringComparison.OrdinalIgnoreCase) == true;
}
