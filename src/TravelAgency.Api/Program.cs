using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using TravelAgency.Api.Data;
using TravelAgency.Api.Services;
using TravelAgency.Shared.Models;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

var provider = builder.Configuration["Database:Provider"] ?? "sqlite";
var sqlServerConnection = builder.Configuration.GetConnectionString("SqlServer");
var supabaseConnection = builder.Configuration.GetConnectionString("Supabase");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider == "sqlserver" && !string.IsNullOrEmpty(sqlServerConnection))
        options.UseSqlServer(sqlServerConnection);
    else if (provider == "supabase" && !string.IsNullOrEmpty(supabaseConnection))
        options.UseNpgsql(supabaseConnection);
    else
        options.UseSqlite("Data Source=travelagency.db");
});

builder.Services.AddSingleton<ImageStorageService>();

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TravelAgencyApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "TravelAgencyApp";
var jwtKey = builder.Configuration["Jwt:Key"] ?? "DevKey_ChangeInProduction_1234567890123456";

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));

var app = builder.Build();

var storage = app.Services.GetRequiredService<ImageStorageService>();
await storage.EnsureBucketAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (provider == "sqlite")
    {
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Notifications" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Notifications" PRIMARY KEY AUTOINCREMENT,
                "UserId" INTEGER NOT NULL,
                "Message" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_Notifications_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS "IX_Notifications_UserId" ON "Notifications" ("UserId");
            """);
    }
    else
    {
        var schemaExists = (await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'Trips'")
            .ToListAsync()).FirstOrDefault() > 0;

        if (!schemaExists)
        {
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
        }
    }
    await EnsureTransportTypeColumnAsync(db, provider);
    await EnsureTripMapColumnsAsync(db, provider);
    await EnsureSeedUsersAsync(db);
}

app.UseHttpsRedirection();

if (!storage.IsSupabase)
{
    var uploadsPath = Path.Combine(app.Environment.WebRootPath ?? Directory.GetCurrentDirectory(), "uploads");
    Directory.CreateDirectory(uploadsPath);

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadsPath),
        RequestPath = "/uploads"
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { Service = "TravelAgency.Api", Status = "OK" }));

app.MapPost("/api/auth/login", async (LoginRequest request, AppDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
    if (user is null) return Results.Unauthorized();

    if (string.IsNullOrEmpty(user.PasswordHash) || !VerifyPassword(request.Password ?? "", user.PasswordHash))
        return Results.Unauthorized();

    return Results.Ok(new AuthResponse
    {
        Token = GenerateToken(user, signingKey, jwtIssuer, jwtAudience),
        UserId = user.Id,
        Name = user.Name,
        Email = user.Email,
        Role = user.Role
    });
});

app.MapPost("/api/auth/register", async (RegisterRequest request, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Nombre, correo y contraseña son obligatorios.");

    if (request.Password.Length < 6)
        return Results.BadRequest("La contraseña debe tener al menos 6 caracteres.");

    var email = request.Email.Trim().ToLowerInvariant();
    if (await db.Users.AnyAsync(u => u.Email == email))
        return Results.Conflict("Ya existe una cuenta con ese correo.");

    var user = new User
    {
        Name = request.Name.Trim(),
        Email = email,
        PasswordHash = HashPassword(request.Password),
        Role = UserRole.Client,
        CreatedAt = DateTime.UtcNow
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created("/api/auth/login", new AuthResponse
    {
        Token = GenerateToken(user, signingKey, jwtIssuer, jwtAudience),
        UserId = user.Id,
        Name = user.Name,
        Email = user.Email,
        Role = user.Role
    });
});

app.MapGet("/api/trips", async (AppDbContext db) =>
    await db.Trips.Where(t => t.IsActive).OrderByDescending(t => t.StartDate).ToListAsync());

app.MapGet("/api/trips/manage", async (AppDbContext db) =>
    await db.Trips
        .Include(t => t.Bookings)
            .ThenInclude(b => b.User)
        .Include(t => t.CapacityRequests)
        .Include(t => t.PointsOfInterest)
        .OrderByDescending(t => t.StartDate)
        .ToListAsync()).RequireAuthorization("AdminOnly");

app.MapGet("/api/trips/{id}", async (int id, AppDbContext db) =>
    await db.Trips
        .Include(t => t.PointsOfInterest)
        .FirstOrDefaultAsync(t => t.Id == id) is Trip trip ? Results.Ok(trip) : Results.NotFound());

app.MapPost("/api/trips", async (Trip trip, AppDbContext db) =>
{
    trip.CreatedAt = DateTime.UtcNow;
    trip.AvailableSeats = trip.Capacity;
    db.Trips.Add(trip);
    await db.SaveChangesAsync();
    return Results.Created($"/api/trips/{trip.Id}", trip);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/trips/{id}/image", async (int id, HttpRequest request, AppDbContext db, ImageStorageService storage) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var file = request.Form.Files.FirstOrDefault();
    if (file is null || file.Length == 0) return Results.BadRequest("No se recibió ninguna imagen.");

    var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (!allowed.Contains(extension)) return Results.BadRequest("Formato no permitido. Usa JPG, PNG o WebP.");

    await using var stream = file.OpenReadStream();
    var imageUrl = await storage.UploadAsync(stream, extension, file.ContentType ?? "application/octet-stream");

    trip.ImageUrl = imageUrl;
    await db.SaveChangesAsync();
    return Results.Ok(trip);
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/trips/{id}", async (int id, Trip input, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var capacityIncreased = input.Capacity > trip.Capacity;

    trip.Title = input.Title;
    trip.Destination = input.Destination;
    trip.Description = input.Description;
    trip.StartDate = input.StartDate;
    trip.EndDate = input.EndDate;
    trip.Price = input.Price;
    trip.Capacity = input.Capacity;
    trip.TransportType = input.TransportType;
    trip.IsActive = input.IsActive;
    trip.OriginLatitude = input.OriginLatitude;
    trip.OriginLongitude = input.OriginLongitude;
    trip.DestinationLatitude = input.DestinationLatitude;
    trip.DestinationLongitude = input.DestinationLongitude;

    await RecomputeAvailabilityAsync(db, trip);
    await db.SaveChangesAsync();

    if (capacityIncreased)
    {
        var pendingRequests = await db.CapacityRequests
            .Where(c => c.TripId == id && !c.IsResolved)
            .ToListAsync();

        var satisfied = 0;
        foreach (var request in pendingRequests)
        {
            if (trip.AvailableSeats < request.RequestedSeats)
                continue;

            db.Notifications.Add(new UserNotification
            {
                UserId = request.UserId,
                Message = $"Se habilitó más cupo para \"{trip.Title}\". ¡Ya puedes reservar con {request.RequestedSeats} asiento(s)!",
                CreatedAt = DateTime.UtcNow
            });
            request.IsResolved = true;
            satisfied++;
        }

        if (satisfied > 0)
            await db.SaveChangesAsync();
    }

    return Results.Ok(trip);
}).RequireAuthorization("AdminOnly");

app.MapDelete("/api/trips/{id}", async (int id, HttpRequest request, AppDbContext db, ImageStorageService storage) =>
{
    var trip = await db.Trips
        .Include(t => t.Bookings)
        .FirstOrDefaultAsync(t => t.Id == id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    string? message = null;
    try
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            var dto = JsonSerializer.Deserialize<DeleteTripRequest>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            message = dto?.Message;
        }
    }
    catch
    {
        // Sin cuerpo JSON: se usa el mensaje por defecto.
    }

    if (string.IsNullOrWhiteSpace(message))
        message = $"Tu reserva para \"{trip.Title}\" fue cancelada porque el viaje fue eliminado.";

    var notified = 0;
    foreach (var booking in trip.Bookings)
    {
        if (booking.Status is BookingStatus.Pending or BookingStatus.Confirmed)
        {
            db.Notifications.Add(new UserNotification
            {
                UserId = booking.UserId,
                Message = message,
                CreatedAt = DateTime.UtcNow
            });
            notified++;
        }
    }

    var bookingIds = trip.Bookings.Select(b => b.Id).ToList();
    var payments = await db.Payments.Where(p => bookingIds.Contains(p.BookingId)).ToListAsync();
    db.Payments.RemoveRange(payments);
    db.Bookings.RemoveRange(trip.Bookings);

    var capacityRequests = await db.CapacityRequests.Where(c => c.TripId == id).ToListAsync();
    db.CapacityRequests.RemoveRange(capacityRequests);

    db.Trips.Remove(trip);
    await db.SaveChangesAsync();

    if (!string.IsNullOrEmpty(trip.ImageUrl))
    {
        await storage.DeleteAsync(trip.ImageUrl);
    }

    return Results.Ok(new { Notified = notified });
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/users", async (AppDbContext db) =>
    await db.Users.ToListAsync()).RequireAuthorization("AdminOnly");

app.MapGet("/api/users/{id}/bookings", async (int id, AppDbContext db, ClaimsPrincipal principal) =>
{
    var tokenUserId = GetUserId(principal);
    if (tokenUserId != id && !principal.IsInRole("Admin"))
        return Results.Forbid();

    return Results.Ok(await db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.Payments)
        .Where(b => b.UserId == id)
        .OrderByDescending(b => b.BookingDate)
        .ToListAsync());
}).RequireAuthorization();

app.MapGet("/api/users/{id}/notifications", async (int id, AppDbContext db, ClaimsPrincipal principal) =>
{
    var tokenUserId = GetUserId(principal);
    if (tokenUserId != id && !principal.IsInRole("Admin"))
        return Results.Forbid();

    return Results.Ok(await db.Notifications
        .Where(n => n.UserId == id)
        .OrderByDescending(n => n.CreatedAt)
        .ToListAsync());
}).RequireAuthorization();

app.MapGet("/api/bookings", async (AppDbContext db) =>
    await db.Bookings
        .Where(b => b.Status == BookingStatus.Pending)
        .Include(b => b.Trip)
        .Include(b => b.User)
        .OrderByDescending(b => b.BookingDate)
        .ToListAsync())
    .RequireAuthorization("AdminOnly");

app.MapPost("/api/bookings", async (Booking booking, AppDbContext db, ClaimsPrincipal principal) =>
{
    var trip = await db.Trips.FindAsync(booking.TripId);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    if (booking.NumberOfSeats < 1)
        return Results.BadRequest("Indica al menos un asiento.");

    if (booking.NumberOfSeats > trip.AvailableSeats)
        return Results.BadRequest("No hay suficientes asientos disponibles.");

    booking.UserId = GetUserId(principal);
    booking.BookingDate = DateTime.UtcNow;
    booking.Status = BookingStatus.Pending;
    booking.TotalAmount = trip.Price * booking.NumberOfSeats;

    db.Bookings.Add(booking);
    await db.SaveChangesAsync();

    await RecomputeAvailabilityAsync(db, trip);
    await db.SaveChangesAsync();

    var userName = await db.Users
        .Where(u => u.Id == booking.UserId)
        .Select(u => u.Name)
        .FirstOrDefaultAsync() ?? "Cliente";
    await SendToAdminsAsync(db, $"Nueva reserva de {userName} para \"{trip.Title}\" ({booking.NumberOfSeats} asiento(s)).");

    return Results.Created($"/api/bookings/{booking.Id}", booking);
}).RequireAuthorization();

app.MapGet("/api/bookings/{id}", async (int id, AppDbContext db, ClaimsPrincipal principal) =>
{
    var booking = await db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.User)
        .Include(b => b.Payments)
        .FirstOrDefaultAsync(b => b.Id == id);

    if (booking is null) return Results.NotFound();

    if (GetUserId(principal) != booking.UserId && !principal.IsInRole("Admin"))
        return Results.Forbid();

    return Results.Ok(booking);
}).RequireAuthorization();

app.MapGet("/api/bookings/{id}/payments", async (int id, AppDbContext db) =>
    await db.Payments.Where(p => p.BookingId == id).ToListAsync()).RequireAuthorization("AdminOnly");

app.MapPost("/api/bookings/{id}/payments", async (int id, Payment payment, AppDbContext db) =>
{
    var booking = await db.Bookings
        .Include(b => b.Trip)
        .FirstOrDefaultAsync(b => b.Id == id);
    if (booking is null) return Results.NotFound("Reserva no encontrada.");

    if (booking.Status == BookingStatus.Cancelled)
        return Results.BadRequest("No se puede registrar el pago de una reserva cancelada.");

    var paid = await db.Payments
        .Where(p => p.BookingId == id)
        .SumAsync(p => p.Amount);
    var remaining = booking.TotalAmount - paid;

    if (payment.Amount <= 0)
        return Results.BadRequest("Indica un monto válido.");

    if (payment.Amount > remaining)
        return Results.BadRequest($"El monto supera el saldo pendiente ({remaining:C}).");

    payment.Amount = Math.Round(payment.Amount, 2);
    payment.BookingId = id;
    payment.PaymentDate = DateTime.UtcNow;
    payment.Status = PaymentStatus.Completed;

    db.Payments.Add(payment);
    paid += payment.Amount;

    var liquidated = paid >= booking.TotalAmount;
    if (liquidated)
        booking.Status = BookingStatus.Confirmed;

    await db.SaveChangesAsync();

    db.Notifications.Add(new UserNotification
    {
        UserId = booking.UserId,
        Message = $"Pago de {payment.Amount:C} recibido. Su pago fue por medio de {PaymentMethodName(payment.Method)}.",
        CreatedAt = DateTime.UtcNow
    });

    if (liquidated)
    {
        db.Notifications.Add(new UserNotification
        {
            UserId = booking.UserId,
            Message = $"Su reserva para \"{booking.Trip?.Title}\" ha sido liquidada y confirmada.",
            CreatedAt = DateTime.UtcNow
        });
    }

    await db.SaveChangesAsync();
    return Results.Created($"/api/bookings/{id}/payments/{payment.Id}", payment);
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/bookings/{id}/status", async (int id, UpdateBookingStatusRequest request, AppDbContext db) =>
{
    var booking = await db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.User)
        .FirstOrDefaultAsync(b => b.Id == id);
    if (booking is null) return Results.NotFound("Reserva no encontrada.");

    if (request.Status == BookingStatus.Confirmed &&
        !await db.Payments.AnyAsync(p => p.BookingId == id))
        return Results.BadRequest("Registra la forma de pago antes de confirmar la reserva.");

    booking.Status = request.Status;
    await db.SaveChangesAsync();

    if (booking.Trip is not null)
    {
        await RecomputeAvailabilityAsync(db, booking.Trip);
        await db.SaveChangesAsync();
    }

    if (request.Status == BookingStatus.Cancelled && booking.User is not null)
        await SendToAdminsAsync(db, $"La reserva de {booking.User.Name} para \"{booking.Trip?.Title}\" fue cancelada.");

    return Results.Ok(booking);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/trips/{id}/capacity-requests", async (int id, CapacityRequest request, AppDbContext db, ClaimsPrincipal principal) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var userId = GetUserId(principal);
    if (userId == 0) return Results.Forbid();

    var entity = new CapacityRequest
    {
        TripId = id,
        UserId = userId,
        RequestedSeats = request.RequestedSeats < 1 ? 1 : request.RequestedSeats,
        Message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim(),
        IsResolved = false,
        CreatedAt = DateTime.UtcNow
    };

    db.CapacityRequests.Add(entity);
    await db.SaveChangesAsync();

    var userName = await db.Users
        .Where(u => u.Id == userId)
        .Select(u => u.Name)
        .FirstOrDefaultAsync() ?? "Cliente";
    await SendToAdminsAsync(db, $"{userName} solicitó {entity.RequestedSeats} asiento(s) para \"{trip.Title}\".");

    return Results.Created($"/api/trips/{id}/capacity-requests/{entity.Id}", entity);
}).RequireAuthorization();

app.MapGet("/api/trips/{id}/capacity-requests", async (int id, AppDbContext db) =>
    await db.CapacityRequests
        .Include(c => c.User)
        .Where(c => c.TripId == id)
        .OrderByDescending(c => c.CreatedAt)
        .ToListAsync()).RequireAuthorization("AdminOnly");

app.MapGet("/api/trips/{id}/bookings", async (int id, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    return Results.Ok(await db.Bookings
        .Include(b => b.User)
        .Include(b => b.Payments)
        .Where(b => b.TripId == id)
        .OrderByDescending(b => b.BookingDate)
        .ToListAsync());
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/trips/{id}/route", async (int id, UpdateTripRouteRequest request, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    trip.OriginLatitude = request.OriginLatitude;
    trip.OriginLongitude = request.OriginLongitude;
    trip.DestinationLatitude = request.DestinationLatitude;
    trip.DestinationLongitude = request.DestinationLongitude;
    await db.SaveChangesAsync();
    return Results.Ok(trip);
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/trips/{id}/pois", async (int id, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    return Results.Ok(await db.TripPointsOfInterest
        .Where(p => p.TripId == id)
        .OrderBy(p => p.Order)
        .ToListAsync());
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/trips/{id}/pois", async (int id, UpsertPoiRequest request, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var order = (await db.TripPointsOfInterest
        .Where(p => p.TripId == id)
        .MaxAsync(p => (int?)p.Order)) + 1 ?? 1;

    var poi = new TripPointOfInterest
    {
        TripId = id,
        Name = string.IsNullOrWhiteSpace(request.Name) ? "Punto de interés" : request.Name.Trim(),
        Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        Order = order
    };

    db.TripPointsOfInterest.Add(poi);
    await db.SaveChangesAsync();
    return Results.Created($"/api/trips/{id}/pois/{poi.Id}", poi);
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/trips/{id}/pois/{poiId}", async (int id, int poiId, UpsertPoiRequest request, AppDbContext db) =>
{
    var poi = await db.TripPointsOfInterest.FirstOrDefaultAsync(p => p.Id == poiId && p.TripId == id);
    if (poi is null) return Results.NotFound("Punto de interés no encontrado.");

    poi.Name = string.IsNullOrWhiteSpace(request.Name) ? poi.Name : request.Name.Trim();
    poi.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
    poi.Latitude = request.Latitude;
    poi.Longitude = request.Longitude;
    await db.SaveChangesAsync();
    return Results.Ok(poi);
}).RequireAuthorization("AdminOnly");

app.MapDelete("/api/trips/{id}/pois/{poiId}", async (int id, int poiId, AppDbContext db) =>
{
    var poi = await db.TripPointsOfInterest.FirstOrDefaultAsync(p => p.Id == poiId && p.TripId == id);
    if (poi is null) return Results.NotFound("Punto de interés no encontrado.");

    db.TripPointsOfInterest.Remove(poi);
    await db.SaveChangesAsync();

    var remaining = await db.TripPointsOfInterest
        .Where(p => p.TripId == id)
        .OrderBy(p => p.Order)
        .ToListAsync();
    for (var i = 0; i < remaining.Count; i++) remaining[i].Order = i + 1;
    await db.SaveChangesAsync();

    return Results.Ok();
}).RequireAuthorization("AdminOnly");

app.Run();

static async Task RecomputeAvailabilityAsync(AppDbContext db, Trip trip)
{
    var sold = await db.Bookings
        .Where(b => b.TripId == trip.Id && b.Status != BookingStatus.Cancelled)
        .SumAsync(b => (int?)b.NumberOfSeats) ?? 0;
    trip.AvailableSeats = trip.Capacity - sold;
}

static string PaymentMethodName(PaymentMethod method) => method switch
{
    PaymentMethod.CreditCard => "tarjeta de crédito",
    PaymentMethod.DebitCard => "tarjeta de débito",
    PaymentMethod.BankTransfer => "transferencia bancaria",
    PaymentMethod.Cash => "efectivo",
    PaymentMethod.PayPal => "PayPal",
    PaymentMethod.MercadoPago => "Mercado Pago",
    _ => "otro medio"
};

static async Task SendToAdminsAsync(AppDbContext db, string message)
{
    var adminIds = await db.Users
        .Where(u => u.Role == UserRole.Admin)
        .Select(u => u.Id)
        .ToListAsync();
    if (adminIds.Count == 0) return;

    foreach (var adminId in adminIds)
    {
        db.Notifications.Add(new UserNotification
        {
            UserId = adminId,
            Message = message,
            CreatedAt = DateTime.UtcNow
        });
    }
    await db.SaveChangesAsync();
}

static string HashPassword(string password)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
    return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
}

static bool VerifyPassword(string password, string stored)
{
    var parts = stored.Split('.');
    if (parts.Length != 2) return false;

    byte[] salt;
    byte[] expected;
    try
    {
        salt = Convert.FromBase64String(parts[0]);
        expected = Convert.FromBase64String(parts[1]);
    }
    catch (FormatException)
    {
        return false;
    }

    var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, expected.Length);
    return CryptographicOperations.FixedTimeEquals(hash, expected);
}

static int GetUserId(ClaimsPrincipal principal)
{
    var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
    return int.TryParse(value, out var id) ? id : 0;
}

static string GenerateToken(User user, SymmetricSecurityKey key, string issuer, string audience)
{
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Name, user.Name ?? ""),
        new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
        new Claim("role", user.Role.ToString())
    };

    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(8),
        signingCredentials: credentials);

    return new JwtSecurityTokenHandler().WriteToken(token);
}

static async Task EnsureTransportTypeColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Trips", "TransportType", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Trips\" ADD COLUMN \"TransportType\" INTEGER NOT NULL DEFAULT 0;"
            : "ALTER TABLE \"Trips\" ADD COLUMN \"TransportType\" integer NOT NULL DEFAULT 0;");
    }

    if (!await ColumnExistsAsync(db, "Trips", "Capacity", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Trips\" ADD COLUMN \"Capacity\" INTEGER NOT NULL DEFAULT 0;"
            : "ALTER TABLE \"Trips\" ADD COLUMN \"Capacity\" integer NOT NULL DEFAULT 0;");

        await TryExecAsync(db, provider == "sqlite"
            ? "UPDATE \"Trips\" SET \"Capacity\" = \"AvailableSeats\" + COALESCE((SELECT SUM(\"NumberOfSeats\") FROM \"Bookings\" WHERE \"Bookings\".\"TripId\" = \"Trips\".\"Id\" AND \"Status\" <> 2), 0);"
            : "UPDATE \"Trips\" t SET \"Capacity\" = t.\"AvailableSeats\" + COALESCE((SELECT SUM(b.\"NumberOfSeats\") FROM \"Bookings\" b WHERE b.\"TripId\" = t.\"Id\" AND b.\"Status\" <> 2), 0);");

        Console.WriteLine("Bootstrap: columna Trips.Capacity agregada y backfilled.");
    }

    await TryExecAsync(db, provider == "sqlite"
        ? """
          CREATE TABLE IF NOT EXISTS "CapacityRequests" (
              "Id" INTEGER NOT NULL CONSTRAINT "PK_CapacityRequests" PRIMARY KEY AUTOINCREMENT,
              "TripId" INTEGER NOT NULL,
              "UserId" INTEGER NOT NULL,
              "RequestedSeats" INTEGER NOT NULL,
              "Message" TEXT NULL,
              "IsResolved" INTEGER NOT NULL DEFAULT 0,
              "CreatedAt" TEXT NOT NULL,
              FOREIGN KEY ("TripId") REFERENCES "Trips" ("Id") ON DELETE RESTRICT,
              FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
          );
          """
        : """
          CREATE TABLE IF NOT EXISTS "CapacityRequests" (
              "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
              "TripId" integer NOT NULL,
              "UserId" integer NOT NULL,
              "RequestedSeats" integer NOT NULL,
              "Message" character varying(1000) NULL,
              "IsResolved" boolean NOT NULL DEFAULT false,
              "CreatedAt" timestamp without time zone NOT NULL,
              CONSTRAINT "FK_CapacityRequests_Trips_TripId" FOREIGN KEY ("TripId") REFERENCES "Trips" ("Id") ON DELETE RESTRICT,
              CONSTRAINT "FK_CapacityRequests_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
          );
          """);

    await TryExecAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_CapacityRequests_TripId\" ON \"CapacityRequests\" (\"TripId\");");
}

static async Task EnsureTripMapColumnsAsync(AppDbContext db, string provider)
{
    var mapColumns = new[]
    {
        "OriginLatitude", "OriginLongitude",
        "DestinationLatitude", "DestinationLongitude"
    };

    foreach (var column in mapColumns)
    {
        if (!await ColumnExistsAsync(db, "Trips", column, provider))
        {
            await TryExecAsync(db, provider == "sqlite"
                ? $"ALTER TABLE \"Trips\" ADD COLUMN \"{column}\" REAL NULL;"
                : $"ALTER TABLE \"Trips\" ADD COLUMN \"{column}\" double precision NULL;");
        }
    }

    await TryExecAsync(db, provider == "sqlite"
        ? """
          CREATE TABLE IF NOT EXISTS "TripPointsOfInterest" (
              "Id" INTEGER NOT NULL CONSTRAINT "PK_TripPointsOfInterest" PRIMARY KEY AUTOINCREMENT,
              "TripId" INTEGER NOT NULL,
              "Name" TEXT NULL,
              "Description" TEXT NULL,
              "Latitude" REAL NOT NULL,
              "Longitude" REAL NOT NULL,
              "Order" INTEGER NOT NULL,
              FOREIGN KEY ("TripId") REFERENCES "Trips" ("Id") ON DELETE RESTRICT
          );
          """
        : """
          CREATE TABLE IF NOT EXISTS "TripPointsOfInterest" (
              "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
              "TripId" integer NOT NULL,
              "Name" character varying(200) NULL,
              "Description" character varying(1000) NULL,
              "Latitude" double precision NOT NULL,
              "Longitude" double precision NOT NULL,
              "Order" integer NOT NULL,
              CONSTRAINT "FK_TripPointsOfInterest_Trips_TripId" FOREIGN KEY ("TripId") REFERENCES "Trips" ("Id") ON DELETE RESTRICT
          );
          """);

    await TryExecAsync(db, provider == "sqlite"
        ? "CREATE INDEX IF NOT EXISTS \"IX_TripPointsOfInterest_TripId\" ON \"TripPointsOfInterest\" (\"TripId\");"
        : "CREATE INDEX IF NOT EXISTS \"IX_TripPointsOfInterest_TripId\" ON \"TripPointsOfInterest\" (\"TripId\");");
}

static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column, string provider)
{
    try
    {
        var sql = provider == "sqlite"
            ? $"SELECT COUNT(*) AS \"Value\" FROM pragma_table_info('{table}') WHERE name = '{column}'"
            : $"SELECT COUNT(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = '{table}' AND column_name = '{column}'";
        return (await db.Database.SqlQueryRaw<int>(sql).ToListAsync()).FirstOrDefault() > 0;
    }
    catch
    {
        return false;
    }
}

static async Task TryExecAsync(AppDbContext db, string sql)
{
    try
    {
        await db.Database.ExecuteSqlRawAsync(sql);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Bootstrap: {ex.Message}");
    }
}

static async Task EnsureSeedUsersAsync(AppDbContext db)
{
    var adminEmail = "admin@travelagency.com";
    var clientEmail = "cliente@travelagency.com";

    var admin = await db.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
    if (admin is null)
    {
        db.Users.Add(new User
        {
            Name = "Administrador",
            Email = adminEmail,
            PasswordHash = HashPassword("Admin123!"),
            Role = UserRole.Admin,
            CreatedAt = DateTime.UtcNow
        });
    }
    else if (admin.PasswordHash == "CHANGE_ME")
    {
        admin.PasswordHash = HashPassword("Admin123!");
    }

    var client = await db.Users.FirstOrDefaultAsync(u => u.Email == clientEmail);
    if (client is null)
    {
        db.Users.Add(new User
        {
            Name = "Cliente Demo",
            Email = clientEmail,
            PasswordHash = HashPassword("Cliente123!"),
            Role = UserRole.Client,
            CreatedAt = DateTime.UtcNow
        });
    }

    await db.SaveChangesAsync();
}

record UpdateBookingStatusRequest(BookingStatus Status);
record DeleteTripRequest(string? Message);
record UpdateTripRouteRequest(double? OriginLatitude, double? OriginLongitude, double? DestinationLatitude, double? DestinationLongitude);
record UpsertPoiRequest(string? Name, string? Description, double Latitude, double Longitude);