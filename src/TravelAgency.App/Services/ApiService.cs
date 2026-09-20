using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Services;

public class ApiService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ApiService()
    {
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    public static string BaseUrl { get; set; } =
#if DEBUG
        "https://localhost:7264";
#else
        "https://tu-servidor-produccion.com";
#endif

    public static string? ResolveUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.StartsWith('/'))
            return BaseUrl + url;
        return url;
    }

    public void SetAuthToken(string? token)
    {
        _http.DefaultRequestHeaders.Authorization =
            string.IsNullOrEmpty(token) ? null : new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/login", request, JsonOptions);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/register", request, JsonOptions);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
    }

    public Task<List<Trip>?> GetTripsAsync() =>
        _http.GetFromJsonAsync<List<Trip>>("/api/trips", JsonOptions);

    public Task<List<Trip>?> GetAdminTripsAsync() =>
        _http.GetFromJsonAsync<List<Trip>>("/api/trips/manage", JsonOptions);

    public Task<List<User>?> GetUsersAsync() =>
        _http.GetFromJsonAsync<List<User>>("/api/users", JsonOptions);

    public async Task<User?> UpdateUserRoleAsync(int userId, UserRole role)
    {
        var response = await _http.PutAsJsonAsync($"/api/users/{userId}/role",
            new UpdateUserRoleRequest { Role = role }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<User>(JsonOptions);
    }

    public Task<List<UserNotification>?> GetNotificationsAsync(int userId) =>
        _http.GetFromJsonAsync<List<UserNotification>>($"/api/users/{userId}/notifications", JsonOptions);

    public Task<Trip?> GetTripAsync(int id) =>
        _http.GetFromJsonAsync<Trip>($"/api/trips/{id}", JsonOptions);

    public Task<List<Booking>?> GetBookingsAsync() =>
        _http.GetFromJsonAsync<List<Booking>>("/api/bookings", JsonOptions);

    public Task<List<Booking>?> GetUserBookingsAsync(int userId) =>
        _http.GetFromJsonAsync<List<Booking>>($"/api/users/{userId}/bookings", JsonOptions);

    public Task<Booking?> GetBookingAsync(int id) =>
        _http.GetFromJsonAsync<Booking>($"/api/bookings/{id}", JsonOptions);

    public Task<List<Booking>?> GetTripBookingsAsync(int tripId) =>
        _http.GetFromJsonAsync<List<Booking>>($"/api/trips/{tripId}/bookings", JsonOptions);

    public Task<AdminStats?> GetAdminStatsAsync() =>
        _http.GetFromJsonAsync<AdminStats>("/api/admin/stats", JsonOptions);

    public Task<List<AuditLog>?> GetAuditLogsAsync(string? entity = null, int? id = null)
    {
        var url = "/api/admin/auditlog";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(entity)) parts.Add($"entity={entity}");
        if (id.HasValue) parts.Add($"id={id.Value}");
        if (parts.Count > 0) url += "?" + string.Join("&", parts);
        return _http.GetFromJsonAsync<List<AuditLog>>(url, JsonOptions);
    }

    public async Task<Trip?> UpdateTripRouteAsync(int tripId, double? originLat, double? originLng, double? destLat, double? destLng)
    {
        var response = await _http.PutAsJsonAsync($"/api/trips/{tripId}/route",
            new { OriginLatitude = originLat, OriginLongitude = originLng, DestinationLatitude = destLat, DestinationLongitude = destLng },
            JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Trip>(JsonOptions);
    }

    public async Task<TripPointOfInterest?> AddPoiAsync(int tripId, TripPointOfInterest poi)
    {
        var response = await _http.PostAsJsonAsync($"/api/trips/{tripId}/pois",
            new { poi.Name, poi.Description, poi.Latitude, poi.Longitude }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TripPointOfInterest>(JsonOptions);
    }

    public async Task<TripPointOfInterest?> UpdatePoiAsync(int tripId, int poiId, TripPointOfInterest poi)
    {
        var response = await _http.PutAsJsonAsync($"/api/trips/{tripId}/pois/{poiId}",
            new { poi.Name, poi.Description, poi.Latitude, poi.Longitude }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TripPointOfInterest>(JsonOptions);
    }

    public async Task DeletePoiAsync(int tripId, int poiId)
    {
        var response = await _http.DeleteAsync($"/api/trips/{tripId}/pois/{poiId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<Trip?> CreateTripAsync(Trip trip)
    {
        var response = await _http.PostAsJsonAsync("/api/trips", trip, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Trip>(JsonOptions);
    }

    public async Task<Trip?> UpdateTripAsync(int id, Trip trip)
    {
        var response = await _http.PutAsJsonAsync($"/api/trips/{id}", trip, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Trip>(JsonOptions);
    }

    public async Task<CapacityRequest?> CreateCapacityRequestAsync(int tripId, int seats, string? message)
    {
        var response = await _http.PostAsJsonAsync($"/api/trips/{tripId}/capacity-requests",
            new CapacityRequest { TripId = tripId, RequestedSeats = seats, Message = message }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CapacityRequest>(JsonOptions);
    }

    public async Task DeleteTripAsync(int id, string? notificationMessage = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/trips/{id}");
        if (!string.IsNullOrEmpty(notificationMessage))
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { Message = notificationMessage }, JsonOptions),
                System.Text.Encoding.UTF8, "application/json");
        }

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Trip?> UploadTripImageAsync(int tripId, FileResult file)
    {
        using var stream = await file.OpenReadAsync();
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
        content.Add(fileContent, "file", Path.GetFileName(file.FileName));

        var response = await _http.PostAsync($"/api/trips/{tripId}/image", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Trip>(JsonOptions);
    }

    public async Task<Booking?> CreateBookingAsync(Booking booking)
    {
        var response = await _http.PostAsJsonAsync("/api/bookings", booking, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Booking>(JsonOptions);
    }

    public async Task<Payment?> CreatePaymentAsync(Payment payment)
    {
        var response = await _http.PostAsJsonAsync($"/api/bookings/{payment.BookingId}/payments", payment, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Payment>(JsonOptions);
    }

    public async Task<Booking?> UpdateBookingStatusAsync(int bookingId, BookingStatus status)
    {
        var response = await _http.PutAsJsonAsync($"/api/bookings/{bookingId}/status",
            new UpdateBookingStatusRequest(status), JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Booking>(JsonOptions);
    }

    private record UpdateBookingStatusRequest(BookingStatus Status);

    public async Task<byte[]> GetBytesAsync(string url)
    {
        using var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<Microsoft.Maui.Controls.ImageSource?> GetTripImageAsync(string? url)
    {
        var absolute = ResolveUrl(url);
        if (absolute is null) return null;

        try
        {
            var bytes = await _http.GetByteArrayAsync(absolute);
            if (bytes.Length == 0) return null;
            return Microsoft.Maui.Controls.ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }
}