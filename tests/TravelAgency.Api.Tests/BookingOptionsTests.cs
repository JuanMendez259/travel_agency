using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelAgency.Api.Data;
using TravelAgency.Shared.Models;
using Xunit;

namespace TravelAgency.Api.Tests;

public sealed class BookingOptionsTests : IAsyncLifetime
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
    public async Task ReservarOpciones_SinAsientos_DevuelveBadRequest()
    {
        var tripId = await CreateTripAsync();
        var option = await GetBaseOptionAsync(tripId);

        var response = await _client.PostAsJsonAsync("/api/bookings", new
        {
            tripId,
            numberOfSeats = 2,
            seatNumber = (int?)null,
            passengers = Array.Empty<object>(),
            options = new[] { new { tripOptionId = option.Id, adults = 2, children = 0 } },
            useOptions = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReservarOpciones_UsaAsientosDerivadosParaValidarPasajeros()
    {
        var tripId = await CreateTripAsync();
        var option = await GetBaseOptionAsync(tripId);

        var response = await _client.PostAsJsonAsync("/api/bookings", new
        {
            tripId,
            numberOfSeats = 0,
            seatNumber = 1,
            passengers = new[] { new { name = "Acompañante", age = 30, seatNumber = 2 } },
            options = new[] { new { tripOptionId = option.Id, adults = 2, children = 0 } },
            useOptions = true,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = await ReadJsonAsync(response);
        Assert.Equal(2, booking.GetProperty("numberOfSeats").GetInt32());
    }

    [Fact]
    public async Task ReservarOpciones_RechazaAcompananteSinEdad()
    {
        var tripId = await CreateTripAsync();
        var option = await GetBaseOptionAsync(tripId);

        var response = await _client.PostAsJsonAsync("/api/bookings", new
        {
            tripId,
            numberOfSeats = 2,
            seatNumber = 1,
            passengers = new[] { new { name = "Sin edad", age = (int?)null, seatNumber = 2 } },
            options = new[] { new { tripOptionId = option.Id, adults = 2, children = 0 } },
            useOptions = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReservarOpciones_RechazaEdadFueraDeRango()
    {
        var tripId = await CreateTripAsync();
        var option = await GetBaseOptionAsync(tripId);

        var response = await _client.PostAsJsonAsync("/api/bookings", new
        {
            tripId,
            numberOfSeats = 2,
            seatNumber = 1,
            passengers = new[] { new { name = "Edad inválida", age = 200, seatNumber = 2 } },
            options = new[] { new { tripOptionId = option.Id, adults = 2, children = 0 } },
            useOptions = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReservarOpciones_RechazaAsientosDuplicados()
    {
        var tripId = await CreateTripAsync();
        var option = await GetBaseOptionAsync(tripId);

        var response = await _client.PostAsJsonAsync("/api/bookings", new
        {
            tripId,
            numberOfSeats = 2,
            seatNumber = 1,
            passengers = new[] { new { name = "Acompañante", age = 30, seatNumber = 1 } },
            options = new[] { new { tripOptionId = option.Id, adults = 2, children = 0 } },
            useOptions = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReservarOpciones_RechazaAsientoOcupado()
    {
        var tripId = await CreateTripAsync();
        var option = await GetBaseOptionAsync(tripId);

        var first = await CreateOptionBookingAsync(tripId, option.Id, seatNumber: 1);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await CreateOptionBookingAsync(tripId, option.Id, seatNumber: 1);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task ReservarOpciones_RechazaFlujoLegacySiHayOpcionesActivas()
    {
        var tripId = await CreateTripAsync();

        var response = await _client.PostAsJsonAsync("/api/bookings", new
        {
            tripId,
            numberOfSeats = 1,
            seatNumber = 1,
            passengers = Array.Empty<object>(),
            options = Array.Empty<object>(),
            useOptions = false,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReservarLegacy_ConOpcionesInactivas_SePermite()
    {
        var tripId = await CreateTripAsync();
        var option = await GetBaseOptionAsync(tripId);
        var deactivate = await DeactivateOptionAsync(tripId, option.Json);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var response = await _client.PostAsJsonAsync("/api/bookings", new
        {
            tripId,
            numberOfSeats = 1,
            seatNumber = 1,
            passengers = Array.Empty<object>(),
            options = Array.Empty<object>(),
            useOptions = false,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CrearOpcion_AsignaOrdenMaximoMasUno()
    {
        var tripId = await CreateTripAsync();

        var response = await _admin.PostAsJsonAsync($"/api/trips/{tripId}/options", new
        {
            name = "VIP",
            description = "Opción VIP",
            priceAdult = 500m,
            priceChild = (decimal?)null,
            benefits = "Acceso preferente",
            isActive = true,
            order = 999,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var option = await ReadJsonAsync(response);
        Assert.Equal(2, option.GetProperty("order").GetInt32());
    }

    [Fact]
    public async Task CrearOpcion_RechazaDescripcionMayorA400()
    {
        var tripId = await CreateTripAsync();

        var response = await _admin.PostAsJsonAsync($"/api/trips/{tripId}/options", new
        {
            name = "Descripción larga",
            description = new string('x', 401),
            priceAdult = 100m,
            priceChild = (decimal?)null,
            benefits = (string?)null,
            isActive = true,
            order = 2,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AgregarPasajero_NoSobrescribeTotalDeOpciones()
    {
        var tripId = await CreateTripAsync(price: 100m);
        var vipResponse = await _admin.PostAsJsonAsync($"/api/trips/{tripId}/options", new
        {
            name = "VIP",
            description = "Opción VIP",
            priceAdult = 500m,
            priceChild = (decimal?)null,
            benefits = (string?)null,
            isActive = true,
            order = 2,
        });
        Assert.Equal(HttpStatusCode.Created, vipResponse.StatusCode);
        var vip = await ReadJsonAsync(vipResponse);
        var vipId = vip.GetProperty("id").GetInt32();

        int bookingId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await db.Users
                .Where(u => u.Email == "cliente@travelagency.com")
                .Select(u => u.Id)
                .SingleAsync();

            var booking = new Booking
            {
                UserId = userId,
                TripId = tripId,
                BookingDate = DateTime.UtcNow,
                Status = BookingStatus.Pending,
                NumberOfSeats = 2,
                SeatNumber = 1,
                TotalAmount = 1000m,
                QrToken = Guid.NewGuid().ToString("N"),
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();

            db.BookingItems.Add(new BookingItem
            {
                BookingId = booking.Id,
                TripOptionId = vipId,
                Adults = 2,
                Children = 0,
                UnitPriceAdult = 500m,
                UnitPriceChild = null,
                OptionName = "VIP",
            });
            await db.SaveChangesAsync();
            bookingId = booking.Id;
        }

        var response = await _client.PostAsJsonAsync($"/api/bookings/{bookingId}/passengers", new
        {
            passengers = new[] { new { name = "Acompañante", age = 30 } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var bookingResponse = await _client.GetAsync($"/api/bookings/{bookingId}");
        Assert.Equal(HttpStatusCode.OK, bookingResponse.StatusCode);
        var updated = await ReadJsonAsync(bookingResponse);
        Assert.Equal(1000m, updated.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task ListadoPublico_IncluyeOpciones()
    {
        var tripId = await CreateTripAsync();

        var response = await _client.GetAsync("/api/trips");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trips = await ReadJsonAsync(response);
        var trip = trips.EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == tripId);

        Assert.True(trip.TryGetProperty("options", out var options));
        Assert.NotEmpty(options.EnumerateArray());
    }

    [Fact]
    public async Task MapaDeAsientos_UsaCupoEfectivoDelHotel()
    {
        var tripId = await CreateTripAsync(
            capacity: 10,
            includesHotel: true,
            hotelCapacity: 5,
            hotelName: "Hotel de prueba");

        var response = await _client.GetAsync($"/api/trips/{tripId}/seats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var map = await ReadJsonAsync(response);

        Assert.Equal(5, map.GetProperty("capacity").GetInt32());
    }

    [Fact]
    public async Task ReservarDosOpciones_GuardaItemsYSumaAsientosYTotal()
    {
        var tripId = await CreateTripAsync(price: 100m);
        var general = await GetBaseOptionAsync(tripId);
        var vipResponse = await _admin.PostAsJsonAsync($"/api/trips/{tripId}/options", new
        {
            name = "VIP",
            description = "Opción VIP",
            priceAdult = 500m,
            priceChild = (decimal?)null,
            benefits = (string?)null,
            isActive = true,
            order = 2,
        });
        Assert.Equal(HttpStatusCode.Created, vipResponse.StatusCode);
        var vip = await ReadJsonAsync(vipResponse);
        var vipId = vip.GetProperty("id").GetInt32();

        var response = await _client.PostAsJsonAsync("/api/bookings", new
        {
            tripId,
            numberOfSeats = 3,
            seatNumber = 1,
            passengers = new[]
            {
                new { name = "Acompañante adulto", age = 30, seatNumber = 2 },
                new { name = "Acompañante niño", age = 5, seatNumber = 3 },
            },
            options = new[]
            {
                new { tripOptionId = general.Id, adults = 1, children = 0 },
                new { tripOptionId = vipId, adults = 1, children = 1 },
            },
            useOptions = true,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = await ReadJsonAsync(response);
        Assert.Equal(3, booking.GetProperty("numberOfSeats").GetInt32());
        Assert.Equal(1100m, booking.GetProperty("totalAmount").GetDecimal());

        var bookingId = booking.GetProperty("id").GetInt32();
        var detailResponse = await _client.GetAsync($"/api/bookings/{bookingId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadJsonAsync(detailResponse);
        var items = detail.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.GetProperty("optionName").GetString() == "General");
        Assert.Contains(items, i => i.GetProperty("optionName").GetString() == "VIP");
    }

    private async Task<HttpResponseMessage> CreateOptionBookingAsync(int tripId, int optionId, int seatNumber)
    {
        return await _client.PostAsJsonAsync("/api/bookings", new
        {
            tripId,
            numberOfSeats = 1,
            seatNumber,
            passengers = Array.Empty<object>(),
            options = new[] { new { tripOptionId = optionId, adults = 1, children = 0 } },
            useOptions = true,
        });
    }

    private async Task<int> CreateTripAsync(
        decimal price = 100m,
        int capacity = 10,
        bool includesHotel = false,
        int? hotelCapacity = null,
        string? hotelName = null)
    {
        var start = DateTime.UtcNow.AddDays(30);
        var response = await _admin.PostAsJsonAsync("/api/trips", new
        {
            title = $"Viaje de opciones {Guid.NewGuid():N}",
            destination = "Destino de prueba",
            description = "Viaje temporal para pruebas de integración",
            startDate = start,
            endDate = start.AddDays(1).AddHours(8),
            price,
            childPrice = (decimal?)null,
            capacity,
            transportType = "Camion",
            isActive = true,
            category = (string?)null,
            bookingDeadline = (DateTime?)null,
            cancellationDaysLimit = 3,
            includesHotel,
            hotelName = includesHotel ? hotelName : null,
            hotelCapacity = includesHotel ? hotelCapacity : null,
            hasTwoFloors = false,
            floor1Capacity = (int?)null,
            floor2Capacity = (int?)null,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var trip = await ReadJsonAsync(response);
        return trip.GetProperty("id").GetInt32();
    }

    private async Task<(int Id, JsonElement Json)> GetBaseOptionAsync(int tripId)
    {
        var response = await _client.GetAsync($"/api/trips/{tripId}/options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadJsonAsync(response);
        var option = options.EnumerateArray().Single(o => o.GetProperty("isBase").GetBoolean());
        return (option.GetProperty("id").GetInt32(), option);
    }

    private async Task<HttpResponseMessage> DeactivateOptionAsync(int tripId, JsonElement option)
    {
        var priceChild = option.GetProperty("priceChild");
        return await _admin.PutAsJsonAsync($"/api/trips/{tripId}/options/{option.GetProperty("id").GetInt32()}", new
        {
            name = option.GetProperty("name").GetString(),
            description = option.GetProperty("description").GetString(),
            priceAdult = option.GetProperty("priceAdult").GetDecimal(),
            priceChild = priceChild.ValueKind == JsonValueKind.Null ? (decimal?)null : priceChild.GetDecimal(),
            benefits = option.GetProperty("benefits").GetString(),
            isActive = false,
            order = option.GetProperty("order").GetInt32(),
        });
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
