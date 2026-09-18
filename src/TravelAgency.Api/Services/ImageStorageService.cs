using System.Net.Http.Headers;

namespace TravelAgency.Api.Services;

public class ImageStorageService
{
    private readonly HttpClient _http = new();
    private readonly string _mode;
    private readonly string? _supabaseUrl;
    private readonly string? _supabaseKey;
    private readonly string _bucket;
    private readonly string _localRoot;

    public ImageStorageService(IConfiguration config, IWebHostEnvironment env)
    {
        _supabaseUrl = config["Supabase:Url"]?.TrimEnd('/');
        _supabaseKey = config["Supabase:ServiceKey"];
        _bucket = string.IsNullOrWhiteSpace(config["Supabase:Bucket"]) ? "trips" : config["Supabase:Bucket"]!;
        _mode = !string.IsNullOrEmpty(_supabaseUrl) && !string.IsNullOrEmpty(_supabaseKey) ? "supabase" : "local";

        _localRoot = Path.Combine(env.WebRootPath ?? Directory.GetCurrentDirectory(), "uploads");
        Directory.CreateDirectory(_localRoot);
    }

    public bool IsSupabase => _mode == "supabase";

    public async Task<string> UploadAsync(Stream stream, string extension, string contentType, CancellationToken ct = default)
        => _mode == "supabase"
            ? await UploadSupabaseAsync(stream, extension, contentType, ct)
            : await UploadLocalAsync(stream, extension, ct);

    public async Task DeleteAsync(string imageUrl, CancellationToken ct = default)
    {
        if (_mode == "supabase")
        {
            await DeleteSupabaseAsync(imageUrl, ct);
            return;
        }
        DeleteLocal(imageUrl);
    }

    public async Task EnsureBucketAsync(CancellationToken ct = default)
    {
        if (_mode != "supabase") return;

        var listUrl = $"{_supabaseUrl}/storage/v1/bucket";
        using var listReq = new HttpRequestMessage(HttpMethod.Get, listUrl);
        Configure(listReq);
        using var listResp = await _http.SendAsync(listReq, ct);
        if (listResp.IsSuccessStatusCode)
        {
            var json = await listResp.Content.ReadAsStringAsync(ct);
            if (json.Contains($"\"id\":\"{_bucket}\"")) return;
        }

        var createUrl = $"{_supabaseUrl}/storage/v1/bucket";
        var payload = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = _bucket,
            ["name"] = _bucket,
            ["public"] = true
        }.ToJsonString();

        using var createReq = new HttpRequestMessage(HttpMethod.Post, createUrl)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        Configure(createReq);
        using var createResp = await _http.SendAsync(createReq, ct);
        if (!createResp.IsSuccessStatusCode)
        {
            var body = await createResp.Content.ReadAsStringAsync(ct);
            Console.Error.WriteLine($"[ImageStorage] No se pudo crear el bucket '{_bucket}': {(int)createResp.StatusCode} {body}");
        }
    }

    private void Configure(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supabaseKey);
        request.Headers.TryAddWithoutValidation("apikey", _supabaseKey);
    }

    private async Task<string> UploadSupabaseAsync(Stream stream, string extension, string contentType, CancellationToken ct)
    {
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var url = $"{_supabaseUrl}/storage/v1/object/{_bucket}/{fileName}";

        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        Configure(request);

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Storage upload falló: {(int)response.StatusCode} {body}");
        }

        return $"{_supabaseUrl}/storage/v1/object/public/{_bucket}/{fileName}";
    }

    private async Task DeleteSupabaseAsync(string imageUrl, CancellationToken ct)
    {
        var fileName = Path.GetFileName(imageUrl);
        if (string.IsNullOrEmpty(fileName)) return;

        var url = $"{_supabaseUrl}/storage/v1/object/{_bucket}/{fileName}";
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        Configure(request);
        await _http.SendAsync(request, ct);
    }

    private async Task<string> UploadLocalAsync(Stream stream, string extension, CancellationToken ct)
    {
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(_localRoot, fileName);

        await using var file = File.Create(filePath);
        await stream.CopyToAsync(file, ct);

        return $"/uploads/{fileName}";
    }

    private void DeleteLocal(string imageUrl)
    {
        var fileName = Path.GetFileName(imageUrl);
        if (string.IsNullOrEmpty(fileName)) return;

        var filePath = Path.Combine(_localRoot, fileName);
        if (File.Exists(filePath)) File.Delete(filePath);
    }
}