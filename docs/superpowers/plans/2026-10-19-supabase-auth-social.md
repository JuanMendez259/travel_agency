# Supabase Auth con proveedores sociales — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDO: usar `superpowers:subagent-driven-development` (recomendado) o `superpowers:executing-plans` para implementar tarea por tarea. Los pasos usan `- [ ]` para seguimiento.

**Goal:** permitir registrarse e iniciar sesión con Google, Apple y Facebook mediante Supabase Auth, sin tocar el correo/contraseña actual ni los roles de la base.

**Architecture:** la app hace el OAuth con Supabase (PKCE) y devuelve a una URL profunda `proximaparadatours://auth`; el token de Supabase se canjea por el JWT de la app en `POST /api/auth/social`. Los roles y el resto de datos siguen en la base de la app.

**Tech Stack:** .NET MAUI 10 (app), ASP.NET Core Minimal API 10 (API), Supabase Auth (GoTrue, HTTP directo), EF Core + Npgsql/SQLite, xunit para pruebas puras.

**Spec:** `docs/superpowers/specs/2026-10-19-supabase-auth-social-design.md`

## Global Constraints

- Textos de UI en español, **sin acentos** ni typos.
- Paleta `DESIGN.md`: `#003B1B` primary, `#14532D`, `#006C49`, `#6CF8BB`, `#FFFFFF` surface-container-lowest, `#F2F3FF`, `#EAEDFF`, `#E2E7FF`, `#DAE2FD`, `#131B2E`, `#404941`, `#717970`, `#BA1A1A`. Iconos con emoji, no Material Symbols. Leer `DESIGN.md` antes de tocar XAML.
- **Sin librerías nuevas**: no `supabase-csharp`. HTTP directo como hace `ImageStorageService`.
- **Sin comentarios** en código C#/XAML.
- `Users` existe en producción: cada campo nuevo exige su `Ensure*ColumnAsync` o la API no arranca.
- Config en la app: constantes estáticas + override por variable de entorno en `MauiProgram`, patrón de `ApiService.BaseUrl` / `TRAVELAGENCY_API_BASEURL`.
- El alta social **solo** crea `Role = Client`. Admin/Coordinador nunca se crean por la vía social.
- Roles y JWT de la app no cambian; la API sigue emitiendo su propio token.

## Review Focus

| Riesgo que el spec implica y ninguna prueba cubre | Qué debe pasar | Dónde se clava |
|---|---|---|
| Token de Supabase inválido/caducado | 401 y mensaje claro, sin reintentos | T2 |
| Correo del proveedor sin verificar (`email_confirmed_at` nulo) | 400 y mensaje claro | T2 |
| Proveedor sin correo | 400 y mensaje claro | T2 |
| `state` del callback no coincide | Se rechaza el callback, sin iniciar sesión | T4 |
| Cancelar el navegador | No crash; vuelve al login con "Cancelaste el inicio de sesión." | T4/T5 |
| Doble pulsación en el proveedor | Una sola cuenta creada (índice único) | T1 |
| Admin/Coordinador entra con social | Siempre 409 "usa tu contraseña"; nunca se crea la cuenta | T2 |
| Deep link en iOS/Android/MacCatalyst | La app vuelve al login tras el proveedor | T7 |

---

### Task 1: Datos — `Users.SupabaseUserId` y `Users.Proveedor`

**Files:**
- Modify: `src/TravelAgency.Shared/Models/User.cs`
- Modify: `src/TravelAgency.Api/Program.cs` (bloque de migraciones junto a `EnsureUserQrColumnAsync`)
- Modify: `src/TravelAgency.Api/appsettings.json` (`Supabase:AnonKey`)

**Interfaces:**
- Consumes: patrón `EnsureUserQrColumnAsync(AppDbContext db, string provider)` y helpers `ColumnExistsAsync` / `TryExecAsync`.
- Produces: `User.SupabaseUserId` (string?), `User.Proveedor` (string?); `EnsureUserSupabaseColumnsAsync(AppDbContext db, string provider)`; config `Supabase:AnonKey`.

- [ ] **Paso 1: Añadir las propiedades a `User`**

En `src/TravelAgency.Shared/Models/User.cs`, tras `QrToken`:
```csharp
public string? SupabaseUserId { get; set; }
public string? Proveedor { get; set; }
```

- [ ] **Paso 2: Añadir `EnsureUserSupabaseColumnsAsync` en `Program.cs`**

Junto a los demás `Ensure*` (después de `EnsureUserEmergencyContactColumnAsync`). Reutiliza `ColumnExistsAsync` y `TryExecAsync` existentes. SQL por proveedor:
```csharp
static async Task EnsureUserSupabaseColumnsAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Users", "SupabaseUserId", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Users\" ADD COLUMN \"SupabaseUserId\" TEXT NULL;"
            : "ALTER TABLE \"Users\" ADD COLUMN \"SupabaseUserId\" character varying(64) NULL;");
    }

    if (!await ColumnExistsAsync(db, "Users", "Proveedor", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Users\" ADD COLUMN \"Proveedor\" TEXT NULL;"
            : "ALTER TABLE \"Users\" ADD COLUMN \"Proveedor\" character varying(16) NULL;");
    }
}
```

- [ ] **Paso 3: Crear los índices únicos (con comprobación previa)**

En el mismo método, tras las columnas. Primero comprobar que no hay correos duplicados en producción:
```sql
SELECT "Email", COUNT(*) FROM "Users" GROUP BY "Email" HAVING COUNT(*) > 1;
```
Si devuelve filas, detenerse y avisar. Si no:
```sql
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users"("Email");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_SupabaseUserId" ON "Users"("SupabaseUserId");
```
En sqlite ambos sin la cláusula parcial.

- [ ] **Paso 4: Registrar la migración en el arranque**

En el bloque `using (var scope = ...)` de `Program.cs`, junto a los demás `Ensure*`:
```csharp
await EnsureUserSupabaseColumnsAsync(db, provider);
```

- [ ] **Paso 5: Añadir `Supabase:AnonKey` a `appsetshots.json` y a Railway**

En `appsettings.json`, objeto `Supabase`, añadir `"AnonKey": ""`. En Railway, variable de entorno `Supabase:AnonKey` con el valor `anon public` de **Project Settings → API**.

- [ ] **Paso 6: Verificar**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Esperado: compilación correcta, 0 errores.

- [ ] **Paso 7: Commit**

```bash
git add src/TravelAgency.Shared/Models/User.cs src/TravelAgency.Api/Program.cs src/TravelAgency.Api/appsettings.json
git commit -m "api: columnas de Supabase en Users y config AnonKey"
```

---

### Task 2: API — `POST /api/auth/social`

**Files:**
- Modify: `src/TravelAgency.Shared/Models/AuthModels.cs`
- Modify: `src/TravelAgency.Api/Program.cs` (endpoint, junto a `MapPost("/api/auth/register")`)

**Interfaces:**
- Consumes: `T1` (`User.SupabaseUserId`, `User.Proveedor`, `Supabase:AnonKey`); helpers `GenerateToken`, `GenerateQrToken`, `GetUserId`, `signingKey`.
- Produces: `SocialLoginRequest { string? AccessToken }`; `SocialAuthResponse { string? Token, int UserId, string? Name, string? Email, UserRole Role, bool PerfilIncompleto, string? Proveedor }`; endpoint `POST /api/auth/social` (público).

- [ ] **Paso 1: Añadir los modelos**

En `src/TravelAgency.Shared/Models/AuthModels.cs`:
```csharp
public class SocialLoginRequest
{
    public string? AccessToken { get; set; }
}

public class SocialAuthResponse
{
    public string? Token { get; set; }
    public int UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public UserRole Role { get; set; }
    public bool PerfilIncompleto { get; set; }
    public string? Proveedor { get; set; }
}
```

- [ ] **Paso 2: Implementar el endpoint**

En `Program.cs`, tras `MapPost("/api/auth/register", ...)`:
```csharp
app.MapPost("/api/auth/social", async (SocialLoginRequest request, AppDbContext db, IConfiguration config) =>
{
    // 1. Validar el token contra Supabase: GET {Supabase:Url}/auth/v1/user
    //    con apikey: <Supabase:AnonKey> y Authorization: Bearer <accessToken>
    //    200 → JSON con id, email, email_confirmed_at, user_metadata, app_metadata.
    //    401 → return Results.Unauthorized()
    // 2. Reglas en orden:
    //    - email nulo o vacío → Results.BadRequest("Tu proveedor no entregó un correo.")
    //    - email_confirmed_at nulo → Results.BadRequest("Tu proveedor no verificó tu correo.")
    //    - sub ya vinculado → GenerateToken(...) + Results.Ok(new SocialAuthResponse { ... PerfilIncompleto = false })
    //    - correo existe sin vincular → Results.Conflict("Ya tienes una cuenta con este correo. Inicia sesión con tu contraseña.")
    //    - cuenta nueva → crear User (Name del proveedor, Email, SupabaseUserId, Proveedor, Role=Client, QrToken=GenerateQrToken(),
    //      Phone/EmergencyContact null) + Results.Ok(new SocialAuthResponse { ... PerfilIncompleto = true })
});
```
Nombre del proveedor: `user_metadata.full_name ?? user_metadata.name ?? email.Split('@')[0]`.
Proveedor normalizado: `app_metadata.provider ?? "unknown"`.
Error de red contra Supabase: `Results.StatusCode(502)` con mensaje "No pudimos contactar con el proveedor de identidad."

- [ ] **Paso 3: Verificar en local**

```bash
tools/uitest/api-local.sh start
curl -s -o /dev/null -w "%{http_code}" -X POST http://127.0.0.1:5199/api/auth/social \
  -H "Content-Type: application/json" -d '{"accessToken":"fake"}'
```
Esperado: **401** (token inválido). Si responde 502, la API local no ve el Supabase de producción: comprobar `Supabase:Url` en `appsettings.json` y la conectividad.

- [ ] **Paso 4: Commit**

```bash
git add src/TravelAgency.Shared/Models/AuthModels.cs src/TravelAgency.Api/Program.cs
git commit -m "api: endpoint de canje de sesión social por JWT de la app"
```

---

### Task 3: API — `PUT /api/users/me`

**Files:**
- Modify: `src/TravelAgency.Shared/Models/AuthModels.cs`
- Modify: `src/TravelAgency.Api/Program.cs` (endpoint, junto a `MapGet("/api/users/me")`)

**Interfaces:**
- Consumes: `GetUserId(ClaimsPrincipal)`.
- Produces: `UpdateProfileRequest { string? Name, string? Phone, string? EmergencyContact }`; endpoint `PUT /api/users/me` (autenticado).

- [ ] **Paso 1: Añadir el modelo**

En `AuthModels.cs`:
```csharp
public class UpdateProfileRequest
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? EmergencyContact { get; set; }
}
```

- [ ] **Paso 2: Implementar el endpoint**

En `Program.cs`, junto a `MapGet("/api/users/me")`:
```csharp
app.MapPut("/api/users/me", async (UpdateProfileRequest request, AppDbContext db, ClaimsPrincipal principal) =>
{
    var user = await db.Users.FindAsync(GetUserId(principal));
    if (user is null) return Results.NotFound("Usuario no encontrado.");

    var phone = request.Phone?.Trim();
    if (string.IsNullOrWhiteSpace(phone) || phone!.Length < 10)
        return Results.BadRequest("Indica un teléfono válido de 10 dígitos.");

    if (!string.IsNullOrWhiteSpace(request.Name)) user.Name = request.Name!.Trim();
    user.Phone = phone;
    user.EmergencyContact = string.IsNullOrWhiteSpace(request.EmergencyContact) ? null : request.EmergencyContact!.Trim();

    await db.SaveChangesAsync();
    return Results.Ok(user);
}).RequireAuthorization();
```

- [ ] **Paso 3: Verificar**

```bash
# sin token → 401
curl -s -o /dev/null -w "%{http_code}" -X PUT http://127.0.0.1:5199/api/users/me \
  -H "Content-Type: application/json" -d '{"phone":"4444579256"}'
# con token de login → 200 y el usuario actualizado
TOKEN=$(curl -s -X POST http://127.0.0.1:5199/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"cliente@travelagency.com","password":"Cliente123!"}' | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
curl -s -X PUT http://127.0.0.1:5199/api/users/me -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"phone":"4444579256"}'
```
Esperado: primer `401`; segundo `200` con `"phone":"4444579256"`.

- [ ] **Paso 4: Commit**

```bash
git add src/TravelAgency.Shared/Models/AuthModels.cs src/TravelAgency.Api/Program.cs
git commit -m "api: endpoint para editar el perfil propio"
```

---

### Task 4: App — `PkceChallenge` puro + pruebas unitarias

**Files:**
- Create: `src/TravelAgency.App/Services/PkceChallenge.cs`
- Create: `src/TravelAgency.App/Services/SocialAuthException.cs`
- Modify: `tests/TravelAgency.App.Tests/TravelAgency.App.Tests.csproj` (enlazar los dos archivos)
- Test: `tests/TravelAgency.App.Tests/PkceChallengeTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `PkceChallenge.CreateVerifier() : string`; `PkceChallenge.CreateChallenge(string verifier) : string`; `PkceChallenge.CreateState() : string`; `PkceChallenge.TryParseAuthCallback(string redirectUrl, string expectedState, out string? code, out string? error) : bool`; `SocialAuthException : Exception` con `bool IsCancelled`.

- [ ] **Paso 1: Escribir las pruebas que fallan**

Crear `tests/TravelAgency.App.Tests/PkceChallengeTests.cs`:
```csharp
using TravelAgency.App.Services;
using Xunit;

namespace TravelAgency.App.Tests;

public class PkceChallengeTests
{
    [Fact]
    public void Challenge_IsS256Base64Url_KnownVector()
    {
        var challenge = PkceChallenge.CreateChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge);
    }

    [Fact]
    public void Verifier_Is43CharsBase64Url_NoPadding()
    {
        var verifier = PkceChallenge.CreateVerifier();
        Assert.Equal(43, verifier.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
    }

    [Fact]
    public void Callback_WithCodeAndMatchingState_ReturnsCode()
    {
        var ok = PkceChallenge.TryParseAuthCallback(
            "proximaparadatours://auth?code=abc123&state=xyz", "xyz", out var code, out var error);
        Assert.True(ok);
        Assert.Equal("abc123", code);
        Assert.Null(error);
    }

    [Fact]
    public void Callback_WithDifferentState_Rejects()
    {
        var ok = PkceChallenge.TryParseAuthCallback(
            "proximaparadatours://auth?code=abc123&state=other", "xyz", out _, out var error);
        Assert.False(ok);
        Assert.Equal("state", error);
    }

    [Fact]
    public void Callback_WithoutCode_Rejects()
    {
        var ok = PkceChallenge.TryParseAuthCallback(
            "proximaparadatours://auth?state=xyz", "xyz", out _, out var error);
        Assert.False(ok);
        Assert.Equal("cancelled", error);
    }
}
```
Añadir al `csproj` de tests:
```xml
<Compile Include="../../src/TravelAgency.App/Services/PkceChallenge.cs" Link="Production/PkceChallenge.cs" />
<Compile Include="../../src/TravelAgency.App/Services/SocialAuthException.cs" Link="Production/SocialAuthException.cs" />
```

- [ ] **Paso 2: Comprobar que fallan**

Run: `dotnet test tests/TravelAgency.App.Tests -v q`
Esperado: fallan las 5 pruebas (tipos no existen).

- [ ] **Paso 3: Implementar**

`PkceChallenge.cs` (sin dependencias de MAUI): `CreateVerifier` genera 32 bytes aleatorios y los codifica Base64Url sin relleno; `CreateChallenge` aplica SHA-256 al verifier y Base64Url; `CreateState` genera 16 bytes; `TryParseAuthCallback` parsea el query con `System.Web.HttpUtility.ParseQueryString` (o `Uri` + `QueryString`), compara el `state` con `ordinal` y devuelve el `code`.

`SocialAuthException.cs`:
```csharp
namespace TravelAgency.App.Services;

public sealed class SocialAuthException : Exception
{
    public bool IsCancelled { get; }

    public SocialAuthException(bool isCancelled, string message) : base(message)
    {
        IsCancelled = isCancelled;
    }
}
```

- [ ] **Paso 4: Comprobar que pasan**

Run: `dotnet test tests/TravelAgency.App.Tests -v q`
Esperado: 13 pruebas en verde (8 previas + 5 nuevas).

- [ ] **Paso 5: Commit**

```bash
git add src/TravelAgency.App/Services/PkceChallenge.cs src/TravelAgency.App/Services/SocialAuthException.cs tests/TravelAgency.App.Tests/
git commit -m "app: PKCE y excepción de auth social con pruebas"
```

---

### Task 5: App — `SupabaseAuthService`

**Files:**
- Create: `src/TravelAgency.App/Services/SupabaseAuthService.cs`
- Modify: `src/TravelAgency.App/Services/ApiService.cs` (`SocialAuthAsync`)
- Modify: `src/TravelAgency.App/MauiProgram.cs` (DI y variables de entorno)

**Interfaces:**
- Consumes: `T4`; `T2` (`SocialAuthResponse`); `ApiService.SocialAuthAsync`.
- Produces: `SupabaseAuthService` (registrado como singleton); `SocialAuthResult { SocialAuthResponse? Auth, string? ProviderEmail, string? Mensaje }`; `ApiService.SocialAuthAsync(string accessToken) : Task<SocialAuthResponse?>`.

- [ ] **Paso 1: Añadir `SocialAuthAsync` a `ApiService`**

```csharp
public async Task<SocialAuthResponse?> SocialAuthAsync(string accessToken)
{
    var response = await _http.PostAsJsonAsync("/api/auth/social", new { accessToken }, JsonOptions);
    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(body) ? "No se pudo iniciar sesión con el proveedor." : body.Trim().Trim('"'));
    }
    return await response.Content.ReadFromJsonAsync<SocialAuthResponse>(JsonOptions);
}
```

- [ ] **Paso 2: Implementar `SupabaseAuthService`**

```csharp
public sealed class SupabaseAuthService
{
    public const string RedirectUri = "proximaparadatours://auth";
    public static string SupabaseUrl { get; set; } = "https://onddztbqjmmybojkspjl.supabase.co";
    public static string SupabaseAnonKey { get; set; } = "";
    private readonly HttpClient _http = new();
    private readonly ApiService _api;

    public SupabaseAuthService(ApiService api) => _api = api;

    public async Task<SocialAuthResult> SignInAsync(string provider)
    {
        var verifier = PkceChallenge.CreateVerifier();
        var challenge = PkceChallenge.CreateChallenge(verifier);
        var state = PkceChallenge.CreateState();

        var authorizeUrl = $"{SupabaseUrl}/auth/v1/authorize"
            + $"?provider={Uri.EscapeDataString(provider)}"
            + $"&redirect_to={Uri.EscapeDataString(RedirectUri)}"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + $"&code_challenge_method=s256"
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
        catch (Exception)
        {
            return new SocialAuthResult { Mensaje = "Cancelaste el inicio de sesión." };
        }

        if (!PkceChallenge.TryParseAuthCallback(result.Url.ToString(), state, out var code, out var parseError))
        {
            return new SocialAuthResult { Mensaje = parseError == "cancelled" ? "Cancelaste el inicio de sesión." : "No pudimos comprobar tu identidad con el proveedor." };
        }

        // Canjear el code por la sesión de Supabase
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post,
            $"{SupabaseUrl}/auth/v1/token?grant_type=pkce");
        tokenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SupabaseAnonKey);
        tokenReq.Content = JsonContent.Create(new { auth_code = code, code_verifier = verifier });
        var tokenRes = await _http.SendAsync(tokenReq);
        if (!tokenRes.IsSuccessStatusCode)
            return new SocialAuthResult { Mensaje = "No pudimos completar el inicio de sesión." };
        using var session = await tokenRes.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = session.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        var providerEmail = session.TryGetProperty("user", out var u) && u.TryGetProperty("email", out var e) ? e.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
            return new SocialAuthResult { Mensaje = "No pudimos completar el inicio de sesión." };

        try
        {
            var auth = await _api.SocialAuthAsync(accessToken);
            return new SocialAuthResult { Auth = auth, ProviderEmail = providerEmail };
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            return new SocialAuthResult { Mensaje = ex.Message, ProviderEmail = providerEmail };
        }
    }
}
```
`SocialAuthResult`:
```csharp
public sealed class SocialAuthResult
{
    public SocialAuthResponse? Auth { get; init; }
    public string? ProviderEmail { get; init; }
    public string? Mensaje { get; init; }
}
```

- [ ] **Paso 3: Registrar el servicio y las variables de entorno**

En `MauiProgram.cs`, junto a los demás `AddSingleton`:
```csharp
builder.Services.AddSingleton<SupabaseAuthService>();
var supabaseUrl = Environment.GetEnvironmentVariable("TRAVELAGENCY_SUPABASE_URL");
if (!string.IsNullOrWhiteSpace(supabaseUrl)) SupabaseAuthService.SupabaseUrl = supabaseUrl;
var supabaseAnonKey = Environment.GetEnvironmentVariable("TRAVELAGENCY_SUPABASE_ANONKEY");
if (!string.IsNullOrWhiteSpace(supabaseAnonKey)) SupabaseAuthService.SupabaseAnonKey = supabaseAnonKey;
```

- [ ] **Paso 4: Verificar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Esperado: compilación correcta, 0 errores.

- [ ] **Paso 5: Commit**

```bash
git add src/TravelAgency.App/Services/SupabaseAuthService.cs src/TravelAgency.App/Services/ApiService.cs src/TravelAgency.App/MauiProgram.cs
git commit -m "app: servicio de auth social con Supabase (PKCE + canje)"
```

---

### Task 6: App — `ProfileSetupPage` y ruta `profilesetup`

**Files:**
- Create: `src/TravelAgency.App/Modules/Auth/Views/ProfileSetupPage.xaml`
- Create: `src/TravelAgency.App/Modules/Auth/Views/ProfileSetupPage.xaml.cs`
- Modify: `src/TravelAgency.App/AppShell.xaml.cs`
- Modify: `src/TravelAgency.App/MauiProgram.cs`

**Interfaces:**
- Consumes: `PhoneNumberFormatter`; `ApiService.UpdateMyProfileAsync`.
- Produces: `ProfileSetupPage(ApiService api, SessionService session)`; ruta `profilesetup`.

- [ ] **Paso 1: Crear la página**

`ProfileSetupPage.xaml`: estilo del login (ver `LoginPage.xaml` y `DESIGN.md`). Nombre en modo lectura (`ProfileNameLabel`), `PhoneEntry` (obligatorio, `Keyboard="Telephone"`), `EmergencyContactEntry` (opcional) y `ContinueButton`. Titulo "Completa tu perfil" y texto breve.

`ProfileSetupPage.xaml.cs`:
```csharp
public ProfileSetupPage(ApiService api, SessionService session)
{
    InitializeComponent();
    ProfileNameLabel.Text = _session.UserName ?? string.Empty;
    // PhoneEntry.TextChanged → PhoneNumberFormatter.Apply(PhoneEntry)
}

private async void OnContinueClicked(object? sender, EventArgs e)
{
    var phone = PhoneNumberFormatter.Digits(PhoneEntry.Text);
    if (phone.Length < 10)
    {
        await DisplayAlertAsync("Teléfono", "Indica un teléfono válido de 10 dígitos.", "OK");
        return;
    }
    ContinueButton.IsEnabled = false;
    try
    {
        await _api.UpdateMyProfileAsync(new UpdateProfileRequest
        {
            Name = ProfileNameLabel.Text,
            Phone = phone,
            EmergencyContact = EmergencyContactEntry.Text?.Trim()
        });
        App.GoToMainShell();
    }
    catch (Exception ex)
    {
        await DisplayAlertAsync("Error", ex.Message, "OK");
    }
    finally
    {
        ContinueButton.IsEnabled = true;
    }
}
```

- [ ] **Paso 2: Registrar la ruta y la página**

En `AppShell.xaml.cs`:
```csharp
Routing.RegisterRoute("profilesetup", typeof(TravelAgency.App.Modules.Auth.Views.ProfileSetupPage));
```
En `MauiProgram.cs`:
```csharp
builder.Services.AddTransient<ProfileSetupPage>();
```

- [ ] **Paso 3: Verificar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Esperado: compilación correcta, 0 errores.

- [ ] **Paso 4: Commit**

```bash
git add src/TravelAgency.App/Modules/Auth/Views/ProfileSetupPage.xaml src/TravelAgency.App/Modules/Auth/Views/ProfileSetupPage.xaml.cs src/TravelAgency.App/AppShell.xaml.cs src/TravelAgency.App/MauiProgram.cs
git commit -m "app: paso de perfil tras el primer ingreso social"
```

---

### Task 7: App — Deep Link `proximaparadatours://`

**Files:**
- Modify: `src/TravelAgency.App/Platforms/iOS/Info.plist`
- Modify: `src/TravelAgency.App/Platforms/Android/MainActivity.cs`

**Interfaces:**
- Consumes: `SupabaseAuthService.RedirectUri`.
- Produces: esquema de URL registrado en iOS/Android/MacCatalyst.

- [ ] **Paso 1: iOS / MacCatalyst (`Info.plist`)**

Antes de `</dict>`, dentro del `dict` raíz:
```xml
<key>CFBundleURLTypes</key>
<array>
    <dict>
        <key>CFBundleURLName</key>
        <string>com.companyname.travelagency.app.auth</string>
        <key>CFBundleURLSchemes</key>
        <array>
            <string>proximaparadatours</string>
        </array>
    </dict>
</array>
```

- [ ] **Paso 2: Android (`MainActivity.cs`)**

Sobre la clase `MainActivity`, antes de `public class MainActivity`:
```csharp
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "proximaparadatours")]
```
El `using Android.Content;` ya está en el archivo.

- [ ] **Paso 3: Verificar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Esperado: compilación correcta, 0 errores.

- [ ] **Paso 4: Commit**

```bash
git add src/TravelAgency.App/Platforms/iOS/Info.plist src/TravelAgency.App/Platforms/Android/MainActivity.cs
git commit -m "app: deep link para el retorno del proveedor de identidad"
```

---

### Task 8: App — `LoginPage`: botones de proveedor e Instagram como enlace

**Files:**
- Modify: `src/TravelAgency.App/Modules/Auth/Views/LoginPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Auth/Views/LoginPage.xaml.cs`
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientProfilePage.xaml.cs` (aviso de teléfono faltante)

**Interfaces:**
- Consumes: `SupabaseAuthService`, `SocialAuthResult`; `T6` (`profilesetup`).
- Produces: login con Google/Apple/Facebook; Instagram como enlace; aviso en perfil.

- [ ] **Paso 1: Reemplazar el bloque social de `LoginPage.xaml`**

Sustituir los botones `FacebookButton` e `InstagramButton` por tres botones con el mismo estilo:
```xml
<Button x:Name="GoogleButton" Text="Continuar con Google" Clicked="OnProviderClicked" ... />
<Button x:Name="AppleButton" Text="Continuar con Apple" Clicked="OnProviderClicked" ... />
<Button x:Name="FacebookButton" Text="Continuar con Facebook" Clicked="OnProviderClicked" ... />
```
Debajo, enlace de texto:
```xml
<Label Text="Síguenos en Instagram" ...>
    <Label.GestureRecognizers>
        <TapGestureRecognizer Tapped="OnInstagramTapped" />
    </Label.GestureRecognizers>
</Label>
```

- [ ] **Paso 2: Añadir el manejo en `LoginPage.xaml.cs`**

Constructor: añadir `SupabaseAuthService social`. En `OnAppearing`, si hay sesión de perfil incompleto, navegar a `profilesetup`.
Nuevo handler:
```csharp
private async void OnProviderClicked(object? sender, EventArgs e)
{
    if ((sender as Button)?.Text is not { Length: > 0 } text) return;
    var provider = text.Contains("Apple", StringComparison.OrdinalIgnoreCase) ? "apple"
        : text.Contains("Facebook", StringComparison.OrdinalIgnoreCase) ? "facebook" : "google";

    SetSocialButtonsEnabled(false);
    try
    {
        var result = await _social.SignInAsync(provider);
        if (result.Auth is null)
        {
            await DisplayAlertAsync("No pudimos iniciar sesión", result.Mensaje ?? "Intenta de nuevo.", "OK");
            if (!string.IsNullOrEmpty(result.ProviderEmail)) EmailEntry.Text = result.ProviderEmail;
            return;
        }

        _session.Start(result.Auth);
        _api.SetAuthToken(result.Auth.Token);
        if (result.Auth.PerfilIncompleto)
            await Shell.Current.GoToAsync("profilesetup");
        else
            App.GoToMainShell();
    }
    catch (Exception ex)
    {
        await DisplayAlertAsync("Error", ex.Message, "OK");
    }
    finally
    {
        SetSocialButtonsEnabled(true);
    }
}
```
`OnInstagramTapped` abre la página de Instagram con `Browser.Default.OpenAsync`.
`SetSocialButtonsEnabled(bool)` activa/desactiva los tres botones.

- [ ] **Paso 3: Aviso de teléfono faltante en `ClientProfilePage`**

En `LoadProfileAsync`, tras cargar el teléfono:
```csharp
if (string.IsNullOrWhiteSpace(user.Phone))
    await DisplayAlertAsync("Tu teléfono", "Aún no has indicado un teléfono. Puedes añadirlo desde la edición de tu perfil.", "OK");
```

- [ ] **Paso 4: Verificar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Esperado: compilación correcta, 0 errores.

- [ ] **Paso 5: Commit**

```bash
git add src/TravelAgency.App/Modules/Auth/Views/LoginPage.xaml src/TravelAgency.App/Modules/Auth/Views/LoginPage.xaml.cs src/TravelAgency.App/Modules/Client/Views/ClientProfilePage.xaml.cs
git commit -m "app: botones de proveedor en el login y aviso de teléfono"
```

---

### Task 9: Verificación final y pruebas manuales

**Files:**
- Ninguno (solo verificación).

**Interfaces:**
- Consumes: todo lo anterior.

- [ ] **Paso 1: Construir todo**

```bash
dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q
dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q
dotnet test tests/TravelAgency.App.Tests -v q
```
Esperado: 0 errores en las dos compilaciones; 13 pruebas en verde.

- [ ] **Paso 2: Comprobar el endpoint con token real (opcional)**

Arrancar la API local y el flujo con un token de Supabase válido; confirmar que devuelve 200 con el JWT de la app y `PerfilIncompleto` en true para cuentas nuevas.

- [ ] **Paso 3: Lista de pruebas manuales**

| Caso | Resultado esperado |
|---|---|
| Login con Google | Entra; si es la primera vez, muestra el paso de perfil |
| Login con Apple | Idem |
| Login con Facebook | Idem |
| Cancelar el navegador | Vuelve al login con "Cancelaste el inicio de sesión." |
| Correo ya existente con contraseña | Alerta "Ya tienes una cuenta…" y el correo rellenado |
| Proveedor sin correo | Alerta "Tu proveedor no entregó un correo." |
| Primer ingreso social | Paso de perfil → teléfono guardado (`GET /api/users/me` lo confirma) |
| Cuenta Admin/Coordinador con social | Alerta "Ya tienes una cuenta…"; no se crea la cuenta |
| Doble pulsación en el proveedor | Una sola cuenta creada |
| Perfil con teléfono faltante | Aviso en `ClientProfilePage` |

- [ ] **Paso 4: Commit de verificación (si hay cambios)**

```bash
git add -A
git commit -m "verificación: build, tests y checklist manual"
```

---

## Fuera de alcance

- Vinculación automática de cuentas existentes por correo.
- Verificación de correo propia para cuentas sociales.
- Persistencia del token de refresco de Supabase en `SecureStorage`.
- Proveedores adicionales (Twitter/X, GitHub, Microsoft, etc.).
- Login social para cuentas Admin/Coordinador.
- Rediseño del login más allá de los botones de proveedor.
