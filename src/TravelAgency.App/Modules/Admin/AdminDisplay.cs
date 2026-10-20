namespace TravelAgency.App.Modules.Admin;

public static class AdminDisplay
{
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return "?";

        var first = FirstLetter(words[0]);
        if (first is null)
            return "?";

        if (words.Length == 1)
            return char.ToUpperInvariant(first.Value).ToString();

        var last = FirstLetter(words[^1]);
        if (last is null)
            return char.ToUpperInvariant(first.Value).ToString();

        return string.Concat(char.ToUpperInvariant(first.Value), char.ToUpperInvariant(last.Value));
    }

    private static char? FirstLetter(string word)
    {
        foreach (var character in word)
        {
            if (char.IsLetter(character))
                return character;
        }

        return null;
    }
}
