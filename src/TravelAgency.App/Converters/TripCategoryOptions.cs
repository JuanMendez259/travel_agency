namespace TravelAgency.App.Converters;

public static class TripCategoryOptions
{
    public const string None = "";

    public static readonly string[] All =
    {
        "Playa",
        "Montaña",
        "Aventura",
        "Cultural",
        "Familiar",
        "Lujo",
        "Bienestar",
        "Negocios",
        "Eventos",
        "Ecoturismo"
    };

    public static readonly string[] PickerOptions = new[] { None }.Concat(All).ToArray();

    public static int IndexOf(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return 0;
        var index = Array.FindIndex(All, c => string.Equals(c, category.Trim(), StringComparison.CurrentCultureIgnoreCase));
        return index < 0 ? 0 : index + 1;
    }

    public static string? FromIndex(int index)
    {
        if (index <= 0 || index > All.Length) return null;
        return All[index - 1];
    }
}
