using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TravelAgency.Api.Tests;

public sealed class TripCancellationTests : IAsyncLifetime
{
    private ApiTestFactory _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiTestFactory();
        _admin = _factory.CreateClient();
        _client = _factory.CreateClient();
        await AuthenticateAsync(_admin, "admin@travelagency.com", "Admin123!");
        await AuthenticateAsync(_client, "cliente@travelagency.com", "Cliente123!");
    }

    public Task DisposeAsync()
    {
        _admin.Dispose();
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CancelarViajePagado_Acredita100AWaller_YBloqueaEliminacion()
    {
        var tripId = await CreateTripAsync(price: 250m);
        var bookingId = await CreateBookingAsync(tripId);
        await RegisterPaymentAsync(bookingId, 100m);

        var response = await _admin.PostAsync($"/api/trips/{tripId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await ReadJsonAsync(response);
        Assert.Equal(1, result.GetProperty("bookingsCancelled").GetInt32());
        Assert.Equal(100m, result.GetProperty("totalRefunded").GetDecimal());

        var booking = await ReadJsonAsync(await _client.GetAsync($"/api/bookings/{bookingId}"));
        Assert.Equal("Cancelled", booking.GetProperty("status").GetString());
        Assert.Equal(1, booking.GetProperty("cancelledByUserId").GetInt32());
        Assert.True(booking.GetProperty("refundTotal").GetDecimal() >= 100m);

        var wallet = await ReadJsonAsync(await _client.GetAsync("/api/users/me/wallet"));
        Assert.Equal(100m, wallet.GetProperty("available").GetDecimal());
        var transactions = wallet.GetProperty("transactions").EnumerateArray().ToList();
        var credit = transactions.Single(t => t.GetProperty("amount").GetDecimal() > 0);
        Assert.Equal(100m, credit.GetProperty("amount").GetDecimal());
        Assert.Equal("CancellationCredit", credit.GetProperty("type").GetString());

        // Segunda cancelación: no vuelve a abonar y responde 400.
        var second = await _admin.PostAsync($"/api/trips/{tripId}/cancel", null);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var walletAfter = await ReadJsonAsync(await _client.GetAsync("/api/users/me/wallet"));
        Assert.Equal(100m, walletAfter.GetProperty("available").GetDecimal());

        // Hay dinero real: el borrado debe ser 409 con mensaje legible.
        var delete = await _admin.DeleteAsync($"/api/trips/{tripId}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        var message = await delete.Content.ReadAsStringAsync();
        Assert.Contains("No se puede eliminar", message);
        Assert.Contains("dinero real", message);
    }

    [Fact]
    public async Task CancelarViaje_NoDuplicaReembolsoDeUnaReservaYaCanceladaPorElCliente()
    {
        var tripId = await CreateTripAsync(price: 250m);
        var cancelledBookingId = await CreateBookingAsync(tripId, seatNumber: 1);
        var activeBookingId = await CreateBookingAsync(tripId, seatNumber: 2);

        var clientCancel = await _client.PostAsJsonAsync(
            $"/api/bookings/{cancelledBookingId}/cancel",
            new { reason = "Cancelación previa del cliente" });
        Assert.Equal(HttpStatusCode.OK, clientCancel.StatusCode);

        await RegisterPaymentAsync(activeBookingId, 50m);
        var tripCancel = await _admin.PostAsync($"/api/trips/{tripId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, tripCancel.StatusCode);

        var result = await ReadJsonAsync(tripCancel);
        Assert.Equal(1, result.GetProperty("bookingsCancelled").GetInt32());
        Assert.Equal(50m, result.GetProperty("totalRefunded").GetDecimal());

        var alreadyCancelled = await ReadJsonAsync(
            await _client.GetAsync($"/api/bookings/{cancelledBookingId}"));
        Assert.Equal("Cancelled", alreadyCancelled.GetProperty("status").GetString());
        Assert.Equal(
            alreadyCancelled.GetProperty("userId").GetInt32(),
            alreadyCancelled.GetProperty("cancelledByUserId").GetInt32());
        Assert.Equal(0m, alreadyCancelled.GetProperty("refundTotal").GetDecimal());

        var wallet = await ReadJsonAsync(await _client.GetAsync("/api/users/me/wallet"));
        Assert.Equal(50m, wallet.GetProperty("available").GetDecimal());
    }

    [Fact]
    public async Task EliminarViajeConReservaCanceladaPorClienteYReembolsoCero_Devuelve200()
    {
        var tripId = await CreateTripAsync(price: 250m);
        var bookingId = await CreateBookingAsync(tripId);

        var cancelBooking = await _client.PostAsJsonAsync(
            $"/api/bookings/{bookingId}/cancel",
            new { reason = "Prueba de cancelación sin pago" });
        Assert.Equal(HttpStatusCode.OK, cancelBooking.StatusCode);

        var delete = await _admin.DeleteAsync($"/api/trips/{tripId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var missing = await _admin.GetAsync($"/api/trips/{tripId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task EliminarViajeConPoisEImagenes_LimpiaRegistrosYArchivos()
    {
        var tripId = await CreateTripAsync(price: 100m);

        var poi = await _admin.PostAsJsonAsync($"/api/trips/{tripId}/pois", new
        {
            name = "Parada de prueba",
            description = "Punto temporal",
            latitude = 20.67,
            longitude = -103.35
        });
        Assert.True(
            poi.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"POI devolvió {(int)poi.StatusCode}: {await poi.Content.ReadAsStringAsync()}");

        using var form = new MultipartFormDataContent();
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var image = new ByteArrayContent(png);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "file", "test.png");

        var upload = await _admin.PostAsync($"/api/trips/{tripId}/image", form);
        Assert.True(
            upload.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Upload devolvió {(int)upload.StatusCode}: {await upload.Content.ReadAsStringAsync()}");
        Assert.Single(Directory.GetFiles(_factory.UploadsRoot));

        var delete = await _admin.DeleteAsync($"/api/trips/{tripId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Empty(Directory.GetFiles(_factory.UploadsRoot));

        var missing = await _admin.GetAsync($"/api/trips/{tripId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ClienteNoPuedeCancelarNiEliminarViaje_Devuelve403()
    {
        var tripId = await CreateTripAsync(price: 100m);

        var cancel = await _client.PostAsync($"/api/trips/{tripId}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);

        var delete = await _client.DeleteAsync($"/api/trips/{tripId}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task ViajeCancelado_NoSePuedeEditarReactivarFinalizarNiAbrirSalida()
    {
        var tripId = await CreateTripAsync(price: 100m);
        var cancel = await _admin.PostAsync($"/api/trips/{tripId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var trip = await ReadJsonAsync(await _admin.GetAsync($"/api/trips/{tripId}"));
        var editPayload = new
        {
            title = trip.GetProperty("title").GetString(),
            destination = trip.GetProperty("destination").GetString(),
            description = trip.GetProperty("description").GetString(),
            startDate = trip.GetProperty("startDate").GetDateTime(),
            endDate = trip.GetProperty("endDate").GetDateTime(),
            price = 100m,
            childPrice = (decimal?)null,
            capacity = 10,
            transportType = "Camion",
            isActive = true,
            category = (string?)null,
            bookingDeadline = (DateTime?)null,
            cancellationDaysLimit = 3,
            includesHotel = false,
            hotelName = (string?)null,
            hotelCapacity = (int?)null,
            hasTwoFloors = false,
            floor1Capacity = (int?)null,
            floor2Capacity = (int?)null
        };

        var edit = await _admin.PutAsJsonAsync($"/api/trips/{tripId}", editPayload);
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);

        var active = await _admin.PutAsJsonAsync($"/api/trips/{tripId}/active", new { isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, active.StatusCode);

        var finalize = await _admin.PutAsJsonAsync($"/api/trips/{tripId}/finalize", new { finalized = true });
        Assert.Equal(HttpStatusCode.BadRequest, finalize.StatusCode);

        var departure = await _admin.PutAsJsonAsync($"/api/trips/{tripId}/departure", new { departureCompleted = true });
        Assert.Equal(HttpStatusCode.BadRequest, departure.StatusCode);
    }

    [Fact]
    public async Task ViajeCancelado_DesapareceDelListadoPublico_PeroSigueEnManage()
    {
        var tripId = await CreateTripAsync(price: 100m);
        var cancel = await _admin.PostAsync($"/api/trips/{tripId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var publicTrips = await ReadJsonAsync(await _client.GetAsync("/api/trips"));
        Assert.DoesNotContain(publicTrips.EnumerateArray(), t => t.GetProperty("id").GetInt32() == tripId);

        var manage = await ReadJsonAsync(await _admin.GetAsync("/api/trips/manage"));
        Assert.Contains(manage.EnumerateArray(), t => t.GetProperty("id").GetInt32() == tripId);
    }

    private async Task<int> CreateTripAsync(decimal price)
    {
        var start = DateTime.UtcNow.AddDays(30);
        var response = await _admin.PostAsJsonAsync("/api/trips", new
        {
            title = $"Viaje de prueba {Guid.NewGuid():N}",
            destination = "Destino de prueba",
            description = "Viaje temporal para pruebas de integración",
            startDate = start,
            endDate = start.AddDays(1).AddHours(8),
            price,
            childPrice = (decimal?)null,
            capacity = 10,
            transportType = "Camion",
            isActive = true,
            category = (string?)null,
            bookingDeadline = (DateTime?)null,
            cancellationDaysLimit = 3,
            includesHotel = false,
            hotelName = (string?)null,
            hotelCapacity = (int?)null,
            hasTwoFloors = false,
            floor1Capacity = (int?)null,
            floor2Capacity = (int?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var trip = await ReadJsonAsync(response);
        return trip.GetProperty("id").GetInt32();
    }

    private async Task<int> CreateBookingAsync(int tripId, int seatNumber = 1)
    {
        var optionId = await GetBaseOptionIdAsync(tripId);
        var response = await _client.PostAsJsonAsync("/api/bookings", new
        {
            tripId,
            numberOfSeats = 1,
            seatNumber,
            passengers = Array.Empty<object>(),
            options = new[] { new { tripOptionId = optionId, adults = 1, children = 0 } },
            useOptions = true,
            walletAmount = (decimal?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = await ReadJsonAsync(response);
        return booking.GetProperty("id").GetInt32();
    }

    private async Task<int> GetBaseOptionIdAsync(int tripId)
    {
        var response = await _admin.GetAsync($"/api/trips/{tripId}/options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadJsonAsync(response);
        return options.EnumerateArray()
            .Single(o => o.GetProperty("isBase").GetBoolean())
            .GetProperty("id")
            .GetInt32();
    }

    private async Task RegisterPaymentAsync(int bookingId, decimal amount)
    {
        var response = await _admin.PostAsJsonAsync($"/api/bookings/{bookingId}/payments", new
        {
            amount,
            method = "Cash"
        });
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Pago devolvió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task AuthenticateAsync(HttpClient http, string email, string password)
    {
        var response = await http.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = document.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}
