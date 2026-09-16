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

    public Task<List<Trip>?> GetTripsAsync() =>
        _http.GetFromJsonAsync<List<Trip>>("/api/trips", JsonOptions);

    public Task<Trip?> GetTripAsync(int id) =>
        _http.GetFromJsonAsync<Trip>($"/api/trips/{id}", JsonOptions);

    public Task<List<Booking>?> GetBookingsAsync() =>
        _http.GetFromJsonAsync<List<Booking>>("/api/bookings", JsonOptions);

    public Task<Booking?> GetBookingAsync(int id) =>
        _http.GetFromJsonAsync<Booking>($"/api/bookings/{id}", JsonOptions);

    public async Task<Trip?> CreateTripAsync(Trip trip)
    {
        var response = await _http.PostAsJsonAsync("/api/trips", trip, JsonOptions);
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
}