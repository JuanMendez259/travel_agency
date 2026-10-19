namespace TravelAgency.App.Services;

public static class AgencyContacts
{
    public const string AgencyWhatsApp = "5214444579256";

    public static string? BuildWhatsAppUrl(string? phone, string? message = null)
    {
        var normalized = PhoneNumberFormatter.Normalize(phone);
        if (normalized is null) return null;

        var url = $"https://wa.me/{normalized}";
        return string.IsNullOrEmpty(message)
            ? url
            : $"{url}?text={Uri.EscapeDataString(message)}";
    }

    public static string? BuildPhoneUrl(string? phone)
    {
        var normalized = PhoneNumberFormatter.Normalize(phone);
        return normalized is null ? null : $"tel:{normalized}";
    }
}
