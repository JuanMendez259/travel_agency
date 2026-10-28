using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TravelAgency.Api.Tests;

/// <summary>
/// Levanta la API real con una base SQLite aislada y un directorio de uploads
/// temporal. Cada test recibe su propia base y no toca producción.
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _previousEnvironment = new();

    public ApiTestFactory()
    {
        // Program lee la configuración de base de datos antes de que
        // ConfigureWebHost se aplique. Las variables de entorno sí llegan a
        // WebApplicationBuilder.Configuration en el arranque.
        SetEnvironment("Database__Provider", "sqlite");
        SetEnvironment("ConnectionStrings__Sqlite", $"Data Source={DatabasePath}");
        SetEnvironment("Supabase__Url", "");
        SetEnvironment("Supabase__ServiceKey", "");
        SetEnvironment("Supabase__AnonKey", "");
        SetEnvironment("Jwt__Key", "TestKey_TravelAgency_Integration_2026_0123456789");
        SetEnvironment("Jwt__Issuer", "TravelAgencyApi");
        SetEnvironment("Jwt__Audience", "TravelAgencyApp");
    }

    public string Root { get; } = Path.Combine(
        Path.GetTempPath(),
        "travelagency-api-tests",
        Guid.NewGuid().ToString("N"));

    public string DatabasePath => Path.Combine(Root, "travelagency-test.db");
    public string WebRoot => Path.Combine(Root, "wwwroot");
    public string UploadsRoot => Path.Combine(WebRoot, "uploads");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(WebRoot);
        Directory.CreateDirectory(UploadsRoot);

        builder.UseEnvironment("Testing");
        builder.UseWebRoot(WebRoot);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "sqlite",
                ["ConnectionStrings:Sqlite"] = $"Data Source={DatabasePath}",
                ["ConnectionStrings:Supabase"] = "",
                ["Supabase:Url"] = "",
                ["Supabase:ServiceKey"] = "",
                ["Supabase:AnonKey"] = "",
                ["Supabase:Bucket"] = "trips",
                ["Jwt:Key"] = "TestKey_TravelAgency_Integration_2026_0123456789",
                ["Jwt:Issuer"] = "TravelAgencyApi",
                ["Jwt:Audience"] = "TravelAgencyApp",
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        foreach (var (name, value) in _previousEnvironment)
            Environment.SetEnvironmentVariable(name, value);
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // El directorio temporal se puede limpiar en una corrida posterior.
        }
    }

    private void SetEnvironment(string name, string value)
    {
        if (!_previousEnvironment.ContainsKey(name))
            _previousEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }
}
