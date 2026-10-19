using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Authentication;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Services;

public sealed class SocialAuthResult
{
    public SocialAuthResponse? Auth { get; init; }
    public string? ProviderEmail { get; init; }
    public string? Mensaje { get; init; }
}

public sealed class SupabaseAuthService
{
    public const string RedirectUri = "proximaparadatours://auth";
    public static string SupabaseUrl { get; set; } = "https://onddztbqjmmybojkspjl.supabase.co";
    public static string SupabaseAnonKey { get; set; } = "";

    private readonly HttpClient _http = new();
    private readonly ApiService _api;

    public SupabaseAuthService(ApiService api)
    {
        _api = api;
    }

    public async Task<SocialAuthResult> SignInAsync(string provider)
    {
        var verifier = PkceChallenge.CreateVerifier();
        var challenge = PkceChallenge.CreateChallenge(verifier);
        var state = PkceChallenge.CreateState();

        var authorizeUrl = $"{SupabaseUrl}/auth/v1/authorize?provider={Uri.EscapeDataString(provider)}"
            + $"&redirect_to={Uri.EscapeDataString(RedirectUri)}"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=s256"
            + $"&state={Uri.EscapeDataString(state)}";

        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = new Uri(authorizeUrl),
                CallbackUrl = new Uri(RedirectUri)
            });
        }
        catch (TaskCanceledException)
        {
            return new SocialAuthResult { Mensaje = "Cancelaste el inicio de sesión." };
        }
        catch (Exception)
        {
            return new SocialAuthResult { Mensaje = "No pudimos abrir el proveedor de identidad." };
        }

        if (!PkceChallenge.TryParseAuthCallback(result.Properties, state, out var code, out var parseError))
        {
            return new SocialAuthResult
            {
                Mensaje = parseError == "cancelled"
                    ? "Cancelaste el inicio de sesión."
                    : "No pudimos comprobar tu identidad con el proveedor."
            };
        }

        using var tokenRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SupabaseUrl}/auth/v1/token?grant_type=pkce");
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SupabaseAnonKey);
        tokenRequest.Headers.TryAddWithoutValidation("apikey", SupabaseAnonKey);
        tokenRequest.Content = JsonContent.Create(new { auth_code = code, code_verifier = verifier });

        HttpResponseMessage tokenResponse;
        try
        {
            tokenResponse = await _http.SendAsync(tokenRequest);
        }
        catch (Exception)
        {
            return new SocialAuthResult { Mensaje = "No pudimos completar el inicio de sesión." };
        }

        using (tokenResponse)
        {
            if (!tokenResponse.IsSuccessStatusCode)
                return new SocialAuthResult { Mensaje = "No pudimos completar el inicio de sesión." };

            var session = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
            var accessToken = session.TryGetProperty("access_token", out var tokenValue)
                ? tokenValue.GetString()
                : null;
            var providerEmail = session.TryGetProperty("user", out var userValue)
                && userValue.TryGetProperty("email", out var emailValue)
                ? emailValue.GetString()
                : null;

            if (string.IsNullOrEmpty(accessToken))
                return new SocialAuthResult { Mensaje = "No pudimos completar el inicio de sesión." };

            try
            {
                var auth = await _api.SocialAuthAsync(accessToken);
                return new SocialAuthResult { Auth = auth, ProviderEmail = providerEmail };
            }
            catch (InvalidOperationException ex)
            {
                return new SocialAuthResult { Mensaje = ex.Message, ProviderEmail = providerEmail };
            }
        }
    }
}
