using System.Globalization;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Services;

public static class TripCalendarIcsFormatter
{
    public static string Build(Trip trip, DateTime createdAtUtc)
    {
        var stamp = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc).ToUniversalTime();
        var lines = new[]
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Proxima Parada Tours//Viajes//ES",
            "CALSCALE:GREGORIAN",
            "BEGIN:VEVENT",
            $"UID:trip-{trip.Id}@proximaparadatours",
            $"DTSTAMP:{stamp.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}",
            $"DTSTART:{trip.StartDate.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)}",
            $"DTEND:{trip.EndDate.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)}",
            $"SUMMARY:{Escape(trip.Title ?? "Viaje con Proxima Parada Tours")}",
            $"LOCATION:{Escape(trip.Destination ?? string.Empty)}",
            $"DESCRIPTION:{Escape(trip.Description ?? string.Empty)}",
            "END:VEVENT",
            "END:VCALENDAR"
        };

        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal);
}
