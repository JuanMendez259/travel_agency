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
using TravelAgency.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

var provider = builder.Configuration["Database:Provider"] ?? "sqlite";
var sqlServerConnection = builder.Configuration.GetConnectionString("SqlServer");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider == "sqlserver" && !string.IsNullOrEmpty(sqlServerConnection))
        options.UseSqlServer(sqlServerConnection);
    else
        options.UseSqlite("Data Source=travelagency.db");
});

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await EnsureSeedUsersAsync(db);
}

app.UseHttpsRedirection();

var uploadsPath = Path.Combine(app.Environment.WebRootPath ?? Directory.GetCurrentDirectory(), "uploads");
Directory.CreateDirectory(uploadsPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

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

app.MapGet("/api/trips/{id}", async (int id, AppDbContext db) =>
    await db.Trips.FindAsync(id) is Trip trip ? Results.Ok(trip) : Results.NotFound());

app.MapPost("/api/trips", async (Trip trip, AppDbContext db) =>
{
    trip.CreatedAt = DateTime.UtcNow;
    db.Trips.Add(trip);
    await db.SaveChangesAsync();
    return Results.Created($"/api/trips/{trip.Id}", trip);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/trips/{id}/image", async (int id, HttpRequest request, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var file = request.Form.Files.FirstOrDefault();
    if (file is null || file.Length == 0) return Results.BadRequest("No se recibió ninguna imagen.");

    var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (!allowed.Contains(extension)) return Results.BadRequest("Formato no permitido. Usa JPG, PNG o WebP.");

    var fileName = $"{Guid.NewGuid():N}{extension}";
    var filePath = Path.Combine(uploadsPath, fileName);

    await using (var stream = File.Create(filePath))
    {
        await file.CopyToAsync(stream);
    }

    trip.ImageUrl = $"/uploads/{fileName}";
    await db.SaveChangesAsync();
    return Results.Ok(trip);
}).RequireAuthorization("AdminOnly");

app.MapDelete("/api/trips/{id}", async (int id, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var bookings = await db.Bookings.Where(b => b.TripId == id).ToListAsync();
    db.Bookings.RemoveRange(bookings);
    db.Trips.Remove(trip);
    await db.SaveChangesAsync();

    if (!string.IsNullOrEmpty(trip.ImageUrl))
    {
        var fileName = Path.GetFileName(trip.ImageUrl);
        var filePath = Path.Combine(uploadsPath, fileName);
        if (File.Exists(filePath)) File.Delete(filePath);
    }

    return Results.NoContent();
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

app.MapGet("/api/bookings", async (AppDbContext db) =>
    await db.Bookings.Include(b => b.Trip).Include(b => b.User).ToListAsync())
    .RequireAuthorization("AdminOnly");

app.MapPost("/api/bookings", async (Booking booking, AppDbContext db, ClaimsPrincipal principal) =>
{
    var trip = await db.Trips.FindAsync(booking.TripId);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    if (booking.NumberOfSeats > trip.AvailableSeats)
        return Results.BadRequest("No hay suficientes asientos disponibles.");

    booking.UserId = GetUserId(principal);
    booking.BookingDate = DateTime.UtcNow;
    booking.Status = BookingStatus.Pending;
    booking.TotalAmount = trip.Price * booking.NumberOfSeats;

    trip.AvailableSeats -= booking.NumberOfSeats;
    db.Bookings.Add(booking);
    await db.SaveChangesAsync();
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
    var booking = await db.Bookings.FindAsync(id);
    if (booking is null) return Results.NotFound("Reserva no encontrada.");

    payment.BookingId = id;
    payment.PaymentDate = DateTime.UtcNow;
    payment.Amount = booking.TotalAmount;
    payment.Status = PaymentStatus.Completed;

    db.Payments.Add(payment);
    booking.Status = BookingStatus.Confirmed;
    await db.SaveChangesAsync();
    return Results.Created($"/api/bookings/{id}/payments/{payment.Id}", payment);
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/bookings/{id}/status", async (int id, UpdateBookingStatusRequest request, AppDbContext db) =>
{
    var booking = await db.Bookings
        .Include(b => b.Trip)
        .FirstOrDefaultAsync(b => b.Id == id);
    if (booking is null) return Results.NotFound("Reserva no encontrada.");

    if (request.Status == BookingStatus.Cancelled && booking.Status != BookingStatus.Cancelled)
        booking.Trip!.AvailableSeats += booking.NumberOfSeats;

    if (request.Status == BookingStatus.Confirmed && booking.Status != BookingStatus.Confirmed
        && booking.Trip!.AvailableSeats < booking.NumberOfSeats)
        return Results.BadRequest("No hay suficientes asientos disponibles.");

    booking.Status = request.Status;
    await db.SaveChangesAsync();
    return Results.Ok(booking);
}).RequireAuthorization("AdminOnly");

app.Run();

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