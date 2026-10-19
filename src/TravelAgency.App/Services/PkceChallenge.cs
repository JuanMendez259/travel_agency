using System.Security.Cryptography;
using System.Text;

namespace TravelAgency.App.Services;

public static class PkceChallenge
{
    private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public static string CreateVerifier()
    {
        var bytes = new byte[32];
        Rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    public static string CreateChallenge(string verifier)
    {
        var verifierBytes = Encoding.UTF8.GetBytes(verifier);
        var hash = SHA256.HashData(verifierBytes);
        return Base64UrlEncode(hash);
    }

    public static string CreateState()
    {
        var bytes = new byte[16];
        Rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    public static bool TryParseAuthCallback(string redirectUrl, string expectedState, out string? code, out string? error)
    {
        code = null;
        error = null;

        // Extract query string after '?'
        var queryStart = redirectUrl.IndexOf('?');
        if (queryStart < 0)
        {
            code = null;
            error = "cancelled";
            return false;
        }

        var query = redirectUrl.Substring(queryStart + 1);
        var parameters = ParseQueryParameters(query);

        var state = parameters.TryGetValue("state", out var stateValue) ? stateValue : "";
        code = parameters.TryGetValue("code", out var codeValue) ? codeValue : null;

        if (string.IsNullOrEmpty(code))
        {
            error = "cancelled";
            return false;
        }

        if (state != expectedState)
        {
            error = "state";
            return false;
        }

        error = null;
        return true;
    }

    private static Dictionary<string, string> ParseQueryParameters(string query)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(query)) return result;

        var parts = query.Split('&');
        foreach (var part in parts)
        {
            var idx = part.IndexOf('=');
            if (idx > 0)
            {
                var key = part.Substring(0, idx);
                var value = part.Substring(idx + 1);
                result[key] = value;
            }
        }
        return result;
    }

    private static string Base64UrlEncode(byte[] data)
    {
        var base64 = Convert.ToBase64String(data);
        var url = base64.Replace('+', '-').Replace('/', '_').Replace("=", "");
        return url;
    }
}