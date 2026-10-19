namespace TravelAgency.App.Services;

public static class PhoneNumberFormatter
{
    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 10) return $"52{digits}";
        if (digits.Length is >= 11 and <= 15) return digits;
        return null;
    }
}
