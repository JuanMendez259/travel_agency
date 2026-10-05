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

static DbContextOptionsBuilder ConfigureDb(DbContextOptionsBuilder options, string? provider, string? sqlServer, string? supabase)
{
    if (provider == "sqlserver" && !string.IsNullOrEmpty(sqlServer))
        options.UseSqlServer(sqlServer);
    else if (provider == "supabase" && !string.IsNullOrEmpty(supabase))
        options.UseNpgsql(supabase);
    else
        options.UseSqlite("Data Source=travelagency.db");

    return options;
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AuditLogInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    ConfigureDb(options, provider, sqlServerConnection, supabaseConnection)
        .AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));

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
            // sin RoleClaimType — el default (ClaimTypes.Role) ya coincide con el mapeo automático
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"))
    .AddPolicy("StaffOnly", policy => policy.RequireRole("Admin", "Coordinador"));

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
    // Antes que cualquier otra migracion: las funciones siguientes materializan
    // Bookings y Payments con LINQ, y EF proyecta todas las columnas mapeadas.
    // Si estas columnas faltan todavia, el SELECT revienta con 42703.
    await EnsureBookingCancellationAuditColumnsAsync(db, provider);
    await EnsurePaymentRefundedAmountColumnAsync(db, provider);
    await EnsurePassengerSeatColumnAsync(db, provider);
    await EnsureBookingSeatColumnAsync(db, provider);
    await EnsureTransportTypeColumnAsync(db, provider);
    await EnsureTripMapColumnsAsync(db, provider);
    await EnsureCheckinColumnsAsync(db, provider);
    await EnsureBookingQrColumnAsync(db, provider);
    await EnsureUserQrColumnAsync(db, provider);
    await EnsureUserEmergencyContactColumnAsync(db, provider);
    await EnsurePassengersTableAsync(db, provider);
    await EnsureTripFinalizedColumnAsync(db, provider);
    await EnsureRatingsTableAsync(db, provider);
    await EnsureChildPriceColumnAsync(db, provider);
    await EnsureBookingDeadlineColumnAsync(db, provider);
    await EnsureTripCategoryColumnAsync(db, provider);
    await EnsureTripHasOptionsColumnAsync(db, provider);
    await EnsureTripOptionsTableAsync(db, provider);
    await EnsureBookingItemsTableAsync(db, provider);
    await EnsurePassengerAgeColumnsAsync(db, provider);
    await EnsureCancellationPolicyColumnAsync(db, provider);
    await EnsureFavoriteTripsTableAsync(db, provider);
    await EnsureAuditLogTableAsync(db, provider);
    await EnsureDefaultTripOptionsBackfillAsync(db, provider);
    await EnsureSeedUsersAsync(db);
}

app.UseHttpsRedirection();

Console.WriteLine($"[ImageStorage] Modo de almacenamiento: {(storage.IsSupabase ? "Supabase" : "local")} " +
    $"| placeholder: {storage.PlaceholderUrl ?? "(sin Supabase:Url)"}");

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
        Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
        EmergencyContact = string.IsNullOrWhiteSpace(request.EmergencyContact) ? null : request.EmergencyContact.Trim(),
        Role = UserRole.Client,
        CreatedAt = DateTime.UtcNow,
        QrToken = GenerateQrToken()
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
        .Include(t => t.Bookings)
            .ThenInclude(b => b.Passengers)
        .Include(t => t.CapacityRequests)
        .Include(t => t.PointsOfInterest)
        .OrderByDescending(t => t.StartDate)
        .ToListAsync()).RequireAuthorization("StaffOnly");

app.MapGet("/api/trips/{id}", async (int id, AppDbContext db) =>
    await db.Trips
        .Include(t => t.PointsOfInterest)
        .FirstOrDefaultAsync(t => t.Id == id) is Trip trip ? Results.Ok(trip) : Results.NotFound());

app.MapGet("/api/trips/{id:int}/seatmap", async (int id, AppDbContext db) =>
{
    var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var capacity = Math.Max(0, trip.Capacity);
    var assigned = await GetOccupiedSeatsAsync(db, id, capacity);

    var map = new TripSeatMap
    {
        TripId = trip.Id,
        TripTitle = trip.Title,
        TransportType = trip.TransportType,
        Capacity = capacity,
        OccupiedCount = assigned.Count
    };

    map.Rows.AddRange(BuildSeatRows(capacity, assigned));
    return Results.Ok(map);
}).RequireAuthorization("StaffOnly");

if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/debug/claims", (ClaimsPrincipal principal) =>
    {
        var claims = principal.Claims.Select(c => new { c.Type, c.Value }).ToList();
        return Results.Ok(new
        {
            IsAuthenticated = principal.Identity?.IsAuthenticated,
            IsAdmin = principal.IsInRole("Admin"),
            Claims = claims
        });
    }).RequireAuthorization("AdminOnly");
}

app.MapGet("/api/trips/{id:int}/seats", async (int id, AppDbContext db) =>
{
    var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var capacity = Math.Max(0, trip.Capacity);
    var occupiedSeats = await GetOccupiedSeatsAsync(db, id, capacity);
    var occupied = occupiedSeats.Keys.OrderBy(n => n).ToList();

    var map = new TripSeatAvailability
    {
        TripId = trip.Id,
        TripTitle = trip.Title,
        TransportType = trip.TransportType,
        Capacity = capacity,
        OccupiedCount = occupied.Count,
        OccupiedSeats = occupied
    };

    var seats = new Dictionary<int, TripSeat>();
    foreach (var number in occupied)
    {
        seats[number] = new TripSeat { Number = number, IsOccupied = true };
    }

    map.Rows.AddRange(BuildSeatRows(capacity, seats));
    return Results.Ok(map);
}).RequireAuthorization();

app.MapPost("/api/trips", async (Trip trip, AppDbContext db, ImageStorageService storage) =>
{
    if (trip.StartDate.Date < DateTime.UtcNow.Date)
        return Results.BadRequest("La fecha de salida no puede estar en el pasado.");
    if (trip.EndDate <= trip.StartDate)
        return Results.BadRequest("La fecha de fin no puede ser anterior (o igual) a la de salida.");
    if (trip.Capacity < 1)
        return Results.BadRequest("La capacidad debe ser de al menos 1 asiento.");
    if (trip.Price < 0)
        return Results.BadRequest("El precio no puede ser negativo.");
    if (trip.ChildPrice < 0)
        return Results.BadRequest("El precio de niño no puede ser negativo.");
    if (trip.BookingDeadline is { } createDeadline && createDeadline.Date > trip.StartDate.Date)
        return Results.BadRequest("La fecha límite de reserva no puede ser posterior a la fecha de salida.");
    if (trip.Category?.Length > 60)
        return Results.BadRequest("La categoría no puede superar los 60 caracteres.");

    trip.CreatedAt = DateTime.UtcNow;
    trip.Category = NormalizeCategory(trip.Category);
    trip.AvailableSeats = trip.Capacity;

    // Sin imagen elegida se usa el placeholder compartido que ya existe en el
    // bucket, para que el viaje siempre tenga una portada consistente.
    if (string.IsNullOrWhiteSpace(trip.ImageUrl) && storage.PlaceholderUrl is not null)
        trip.ImageUrl = storage.PlaceholderUrl;

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

    if (file.Length > TripImageProcessor.MaxInputBytes)
        return Results.BadRequest($"La imagen no puede superar los {TripImageProcessor.MaxInputBytes / (1024 * 1024)} MB.");

    await using var input = new MemoryStream();
    await file.CopyToAsync(input);

    Stream uploadStream;
    var ownStream = false;
    string contentType;
    try
    {
        var prepared = TripImageProcessor.Prepare(input, extension);
        uploadStream = prepared.Stream;
        ownStream = prepared.Owned;
        contentType = prepared.ContentType;
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);
    }

    try
    {
        var imageUrl = await storage.UploadAsync(uploadStream, extension, contentType);

        var previousImageUrl = trip.ImageUrl;
        trip.ImageUrl = imageUrl;
        await db.SaveChangesAsync();

        // La BD ya apunta a la nueva imagen, asi que ahora es seguro borrar la anterior.
        if (!string.IsNullOrEmpty(previousImageUrl) &&
            !string.Equals(previousImageUrl, imageUrl, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await storage.DeleteAsync(previousImageUrl);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[ImageStorage] No se pudo borrar la imagen anterior del viaje {id}: {ex.Message}");
            }
        }

        return Results.Ok(trip);
    }
    finally
    {
        if (ownStream && uploadStream is not null) await uploadStream.DisposeAsync();
    }
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/trips/{id}", async (int id, Trip input, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    if (input.StartDate.Date < DateTime.UtcNow.Date)
        return Results.BadRequest("La fecha de salida no puede estar en el pasado.");
    if (input.EndDate <= input.StartDate)
        return Results.BadRequest("La fecha de fin no puede ser anterior (o igual) a la de salida.");
    if (input.Capacity < 1)
        return Results.BadRequest("La capacidad debe ser de al menos 1 asiento.");
    if (input.Price < 0)
        return Results.BadRequest("El precio no puede ser negativo.");
    if (input.ChildPrice < 0)
        return Results.BadRequest("El precio de niño no puede ser negativo.");
    if (input.BookingDeadline is { } editDeadline && editDeadline.Date > input.StartDate.Date)
        return Results.BadRequest("La fecha límite de reserva no puede ser posterior a la fecha de salida.");
    if (input.Category?.Length > 60)
        return Results.BadRequest("La categoría no puede superar los 60 caracteres.");

    var capacityIncreased = input.Capacity > trip.Capacity;

    trip.Title = input.Title;
    trip.Destination = input.Destination;
    trip.Description = input.Description;
    trip.StartDate = input.StartDate;
    trip.EndDate = input.EndDate;
    trip.Price = input.Price;
    trip.ChildPrice = input.ChildPrice;
    trip.Capacity = input.Capacity;
    trip.TransportType = input.TransportType;
    trip.IsActive = input.IsActive;
    // El limite de cancelacion es obligatorio: si no llega, se aplica el valor por defecto.
    trip.CancellationDaysLimit = input.CancellationDaysLimit is >= 0
        ? input.CancellationDaysLimit
        : BookingRefundPolicy.DefaultCancellationDaysLimit;
    trip.BookingDeadline = input.BookingDeadline?.Date;
    trip.Category = NormalizeCategory(input.Category);
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

app.MapGet("/api/users/me", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var user = await db.Users.FindAsync(GetUserId(principal));
    return user is null ? Results.NotFound("Usuario no encontrado.") : Results.Ok(user);
}).RequireAuthorization();

app.MapGet("/api/users/me/stats", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var trips = await db.Bookings.CountAsync(b => b.UserId == userId && b.Status != BookingStatus.Cancelled);
    var reviews = await db.TripRatings.CountAsync(r => r.UserId == userId);
    return Results.Ok(new ProfileStats { Trips = trips, Reviews = reviews });
}).RequireAuthorization();

app.MapPut("/api/users/{id}/role", async (int id, UpdateUserRoleRequest request, AppDbContext db, ClaimsPrincipal principal) =>
{
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound("Usuario no encontrado.");

    if (!Enum.IsDefined(typeof(UserRole), request.Role))
        return Results.BadRequest("Rol inválido.");

    var actorId = GetUserId(principal);
    if (user.Id == actorId)
        return Results.BadRequest("No puedes cambiar tu propio rol.");

    if (user.Role == UserRole.Admin && request.Role != UserRole.Admin)
    {
        var adminsCount = await db.Users.CountAsync(u => u.Role == UserRole.Admin);
        if (adminsCount <= 1)
            return Results.BadRequest("Debe quedar al menos un administrador.");
    }

    user.Role = request.Role;
    await db.SaveChangesAsync();
    return Results.Ok(user);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/users/coordinators", async (CreateCoordinatorRequest request, AppDbContext db) =>
{
    var name = request.Name?.Trim();
    var email = request.Email?.Trim().ToLowerInvariant();

    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest("El nombre del coordinador es obligatorio.");

    if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        return Results.BadRequest("El correo no es válido.");

    if (await db.Users.AnyAsync(u => u.Email == email))
        return Results.Conflict("Ya existe una cuenta con ese correo.");

    var password = string.IsNullOrWhiteSpace(request.Password)
        ? GenerateTemporaryPassword()
        : request.Password.Trim();

    if (password.Length < 6)
        return Results.BadRequest("La contraseña debe tener al menos 6 caracteres.");

    var user = new User
    {
        Name = name.Length <= 120 ? name : name[..120],
        Email = email,
        PasswordHash = HashPassword(password),
        Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
        Role = UserRole.Coordinador,
        CreatedAt = DateTime.UtcNow,
        QrToken = GenerateQrToken()
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/api/users/{user.Id}", new CreatedCoordinator
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        Phone = user.Phone,
        Role = user.Role,
        CreatedAt = user.CreatedAt,
        TemporaryPassword = password
    });
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/users/{id}/bookings", async (int id, AppDbContext db, ClaimsPrincipal principal, string? status) =>
{
    var tokenUserId = GetUserId(principal);
    if (tokenUserId != id && !principal.IsInRole("Admin"))
        return Results.Forbid();

    var query = db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.Payments)
        .Include(b => b.Passengers)
        .Where(b => b.UserId == id);

    if (TryParseBookingStatus(status, out var parsed))
        query = query.Where(b => b.Status == parsed);

    return Results.Ok(await query.OrderByDescending(b => b.BookingDate).ToListAsync());
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

app.MapGet("/api/bookings", async (AppDbContext db, string? status) =>
{
    var query = db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.User)
        .Include(b => b.Passengers)
        .AsQueryable();

    if (TryParseBookingStatus(status, out var parsed))
        query = query.Where(b => b.Status == parsed);

    return Results.Ok(await query.OrderByDescending(b => b.BookingDate).ToListAsync());
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/bookings", async (CreateBookingRequest request, AppDbContext db, ClaimsPrincipal principal) =>
{
    var trip = await db.Trips.FindAsync(request.TripId);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    if (!trip.IsActive)
        return Results.BadRequest("El viaje está pausado y no acepta nuevas reservas.");

    var bookingDeadline = trip.BookingDeadline ?? trip.StartDate;
    if (DateTime.Today > bookingDeadline.Date)
        return Results.BadRequest($"Este viaje cerró reservas el {bookingDeadline:dd/MM/yyyy}.");

    if (request.NumberOfSeats < 1)
        return Results.BadRequest("Indica al menos un asiento.");

    if (request.NumberOfSeats > trip.AvailableSeats)
        return Results.BadRequest("No hay suficientes asientos disponibles.");

    var requestedSeats = new List<int>();
    if (request.SeatNumber.HasValue) requestedSeats.Add(request.SeatNumber.Value);
    if (request.Passengers is { Count: > 0 })
        requestedSeats.AddRange(request.Passengers.Where(p => p.SeatNumber.HasValue).Select(p => p.SeatNumber!.Value));

    if (requestedSeats.Count == 0)
        return Results.BadRequest("Debes seleccionar los asientos antes de confirmar la reserva.");

    if (requestedSeats.Count != request.NumberOfSeats)
        return Results.BadRequest($"Debes elegir exactamente {request.NumberOfSeats} asiento(s).");

    if (requestedSeats.Distinct().Count() != requestedSeats.Count)
        return Results.BadRequest("No puedes elegir el mismo asiento dos veces.");

    if (requestedSeats.Any(s => s < 1 || s > trip.Capacity))
        return Results.BadRequest("Uno de los asientos seleccionados no existe en este viaje.");

    var alreadyTaken = await GetTakenSeatNumbersAsync(db, trip);
    if (requestedSeats.Any(alreadyTaken.Contains))
        return Results.BadRequest("Alguno de los asientos seleccionados ya fue ocupado. Elige otros en el mapa.");

    var passengers = new List<TripPassenger>();
    if (request.Passengers is { Count: > 0 })
    {
        if (request.Passengers.Count > request.NumberOfSeats - 1)
            return Results.BadRequest($"Esta reserva admite máximo {request.NumberOfSeats - 1} pasajero(s) adicional(es).");

        foreach (var input in request.Passengers)
        {
            var name = input.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest("Todos los pasajeros deben tener nombre.");

            passengers.Add(new TripPassenger
            {
                Name = name.Length <= 120 ? name : name[..120],
                Age = input.Age,
                IsChild = IsChildAge(input.Age),
                SeatNumber = input.SeatNumber,
                QrToken = GenerateQrToken()
            });
        }
    }
    else if (request.NumberOfSeats > 1)
    {
        // reserva multiasiento sin pasajeros: se permite crear y agregarlos después
    }

    var booking = new Booking
    {
        UserId = GetUserId(principal),
        TripId = request.TripId,
        NumberOfSeats = request.NumberOfSeats,
        SeatNumber = request.SeatNumber,
        BookingDate = DateTime.UtcNow,
        Status = BookingStatus.Pending,
        QrToken = GenerateQrToken()
    };
    booking.TotalAmount = ComputeBookingTotal(trip, passengers);

    db.Bookings.Add(booking);
    await db.SaveChangesAsync();

    // Save booking items if options provided (multi-item mode)
    var bookingItems = new List<TravelAgency.Shared.Models.BookingItem>();
    var useOptions = request.UseOptions == true || (request.Options != null && request.Options.Count > 0) || trip.HasOptions;
    if (useOptions && request.Options != null && request.Options.Count > 0)
    {
        var optionIds = request.Options.Select(o => o.TripOptionId).Distinct().ToList();
        var opts = await db.TripOptions.Where(o => o.TripId == trip.Id && optionIds.Contains(o.TripId == o.TripId ? o.Id : o.Id)).ToListAsync(); // simple
        var optsDict = await db.TripOptions.Where(o => o.TripId == trip.Id && optionIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id);
        foreach (var oi in request.Options)
        {
            if (!optsDict.TryGetValue(oi.TripOptionId, out var opt)) continue;
            var adults = Math.Max(0, oi.Adults);
            var children = Math.Max(0, oi.Children);
            var seatsOpt = adults + children;
            if (seatsOpt <= 0) continue;
            if (seatsOpt > opt.AvailableSeats && opt.AvailableSeats >= 0) { /* allow? but check later */ }
            bookingItems.Add(new TravelAgency.Shared.Models.BookingItem
            {
                BookingId = booking.Id,
                TripOptionId = opt.Id,
                Adults = adults,
                Children = children,
                UnitPriceAdult = opt.PriceAdult,
                UnitPriceChild = opt.PriceChild,
                OptionName = opt.Name
            });
        }
        if (bookingItems.Count > 0)
        {
            db.BookingItems.AddRange(bookingItems);
            await db.SaveChangesAsync();
            booking.TotalAmount = ComputeBookingTotal(trip, passengers, bookingItems);
            await db.SaveChangesAsync();
        }
    }

    if (passengers.Count > 0)
    {
        await AssignFreeSeatsAsync(db, trip, passengers, booking.Id);
        foreach (var p in passengers) p.BookingId = booking.Id;
        db.TripPassengers.AddRange(passengers);
        await db.SaveChangesAsync();
    }

    await RecomputeAvailabilityAsync(db, trip);
    await db.SaveChangesAsync();

    var userName = await db.Users
        .Where(u => u.Id == booking.UserId)
        .Select(u => u.Name)
        .FirstOrDefaultAsync() ?? "Cliente";
    await SendToStaffAsync(db, $"Nueva reserva de {userName} para \"{trip.Title}\" ({booking.NumberOfSeats} asiento(s)).");

    return Results.Created($"/api/bookings/{booking.Id}", booking);
}).RequireAuthorization();

app.MapPost("/api/bookings/{id}/passengers", async (int id, CreatePassengersRequest request, AppDbContext db, ClaimsPrincipal principal) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
    if (booking is null) return Results.NotFound("Reserva no encontrada.");

    if (booking.Status == BookingStatus.Cancelled)
        return Results.BadRequest("No se pueden agregar pasajeros a una reserva cancelada.");

    var userId = GetUserId(principal);
    if (userId != booking.UserId && !principal.IsInRole("Admin") && !principal.IsInRole("Coordinador"))
        return Results.Forbid();

    if (request.Passengers is null || request.Passengers.Count == 0)
        return Results.BadRequest("Indica al menos un pasajero.");

    var existing = await db.TripPassengers.CountAsync(p => p.BookingId == id);
    var availableSlots = booking.NumberOfSeats - 1 - existing;

    if (request.Passengers.Count > availableSlots)
        return Results.BadRequest($"Esta reserva admite máximo {availableSlots} pasajero(s) adicional(es).");

    var trip = await db.Trips.FindAsync(booking.TripId);

    var passengers = new List<TripPassenger>();
    foreach (var input in request.Passengers)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest("Todos los pasajeros deben tener nombre.");

        passengers.Add(new TripPassenger
        {
            BookingId = id,
            Name = name.Length <= 120 ? name : name[..120],
            Age = input.Age,
            IsChild = IsChildAge(input.Age),
            QrToken = GenerateQrToken()
        });
    }

    if (trip is not null)
    {
        await AssignFreeSeatsAsync(db, trip, passengers, id);
    }

    db.TripPassengers.AddRange(passengers);
    await db.SaveChangesAsync();

    if (trip is not null)
    {
        var all = await db.TripPassengers.Where(p => p.BookingId == id).ToListAsync();
        booking.TotalAmount = ComputeBookingTotal(trip, all);
        await db.SaveChangesAsync();
    }

    return Results.Created($"/api/bookings/{id}/passengers", passengers);
}).RequireAuthorization();

app.MapGet("/api/bookings/{id}", async (int id, AppDbContext db, ClaimsPrincipal principal) =>
{
    var booking = await db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.User)
        .Include(b => b.Payments)
        .Include(b => b.Passengers)
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

app.MapPut("/api/bookings/{id}/status", async (int id, UpdateBookingStatusRequest request, AppDbContext db, ClaimsPrincipal principal) =>
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
    if (request.Status == BookingStatus.Cancelled)
    {
        // Queda registrado que fue la agencia, no el cliente, para que la app
        // no le diga al usuario que el cancelo el.
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancelledByUserId = GetUserId(principal);
    }
    else
    {
        booking.CancelledAt = null;
        booking.CancelledByUserId = null;
    }

    await db.SaveChangesAsync();

    if (booking.Trip is not null)
    {
        await RecomputeAvailabilityAsync(db, booking.Trip);
        await db.SaveChangesAsync();
    }

    if (request.Status == BookingStatus.Cancelled && booking.User is not null)
        await SendToStaffAsync(db, $"La reserva de {booking.User.Name} para \"{booking.Trip?.Title}\" fue cancelada.");

    return Results.Ok(booking);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/bookings/{id}/cancel", async (int id, AppDbContext db, ClaimsPrincipal principal) =>
{
    var booking = await db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.User)
        .Include(b => b.Payments)
        .FirstOrDefaultAsync(b => b.Id == id);
    if (booking is null) return Results.NotFound("Reserva no encontrada.");

    var userId = GetUserId(principal);
    if (userId == 0 || booking.UserId != userId) return Results.Forbid();

    if (booking.Status == BookingStatus.Cancelled)
        return Results.BadRequest("La reserva ya está cancelada.");

    var trip = booking.Trip;
    if (trip is null) return Results.NotFound("El viaje de esta reserva ya no existe.");

    if (trip.DepartureCompleted)
        return Results.BadRequest("La salida ya se realizó, no es posible cancelar.");
    if (trip.Finalized)
        return Results.BadRequest("El viaje ya finalizó, no es posible cancelar.");

    var today = DateTime.UtcNow.Date;
    if (trip.StartDate.Date <= today)
        return Results.BadRequest("El viaje ya comenzó, no es posible cancelar.");

    var limit = trip.CancellationDaysLimit;
    var remainingDays = (trip.StartDate.Date - today).Days;
    // Dentro del limite del viaje se reembolsa el 100% de lo abonado; fuera, aplica multa del 30%.
    var withinPolicy = BookingRefundPolicy.IsWithinPolicy(trip.StartDate, today, limit);

    var completedPayments = booking.Payments
        .Where(p => p.Status == PaymentStatus.Completed)
        .ToList();
    var paid = completedPayments.Sum(p => p.Amount);

    // Multa del 30% solo cuando se cancela fuera del limite de dias del viaje.
    var refund = BookingRefundPolicy.RefundFrom(paid, withinPolicy);
    var penalty = BookingRefundPolicy.PenaltyFrom(paid, withinPolicy);

    // El reembolso se reparte entre los pagos en proporcion a su monto. El ultimo
    // absorbe el redondeo para que la suma coincida exactamente con el total.
    var pending = refund;
    for (var i = 0; i < completedPayments.Count; i++)
    {
        var payment = completedPayments[i];
        var share = i == completedPayments.Count - 1
            ? pending
            : Math.Min(payment.Amount, Math.Round(refund * (payment.Amount / paid), 2));

        payment.Status = PaymentStatus.Refunded;
        payment.RefundedAmount = share;
        pending -= share;
    }

    booking.Status = BookingStatus.Cancelled;
    booking.CancelledAt = DateTime.UtcNow;
    booking.CancelledByUserId = userId;

    await db.SaveChangesAsync();
    await RecomputeAvailabilityAsync(db, trip);
    await db.SaveChangesAsync();

    var policy = withinPolicy
        ? $"dentro del límite de {limit ?? 0} día(s), sin multa"
        : $"fuera del límite de {limit} día(s) (faltan {remainingDays}), multa 30%: {penalty:C}";

    if (booking.User is not null)
        await SendToStaffAsync(db,
            $"El cliente {booking.User.Name} canceló su reserva para \"{trip.Title}\". Reembolso {refund:C} ({policy}).");

    return Results.Ok(new CancelBookingResult(booking, refund, penalty, withinPolicy));
}).RequireAuthorization();

app.MapGet("/api/favorites", async (AppDbContext db, ClaimsPrincipal principal) =>
{
    var userId = GetUserId(principal);
    if (userId == 0) return Results.Forbid();

    var trips = await db.FavoriteTrips
        .Where(f => f.UserId == userId)
        .OrderByDescending(f => f.CreatedAt)
        .Select(f => f.Trip!)
        .ToListAsync();
    return Results.Ok(trips);
}).RequireAuthorization();

app.MapPost("/api/favorites/{tripId}", async (int tripId, AppDbContext db, ClaimsPrincipal principal) =>
{
    var userId = GetUserId(principal);
    if (userId == 0) return Results.Forbid();

    if (!await db.Trips.AnyAsync(t => t.Id == tripId))
        return Results.NotFound("Viaje no encontrado.");

    var exists = await db.FavoriteTrips.AnyAsync(f => f.UserId == userId && f.TripId == tripId);
    if (!exists)
    {
        db.FavoriteTrips.Add(new FavoriteTrip
        {
            UserId = userId,
            TripId = tripId,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    return Results.Ok(new { IsFavorite = true });
}).RequireAuthorization();

app.MapDelete("/api/favorites/{tripId}", async (int tripId, AppDbContext db, ClaimsPrincipal principal) =>
{
    var userId = GetUserId(principal);
    if (userId == 0) return Results.Forbid();

    var favorite = await db.FavoriteTrips
        .FirstOrDefaultAsync(f => f.UserId == userId && f.TripId == tripId);
    if (favorite is not null)
    {
        db.FavoriteTrips.Remove(favorite);
        await db.SaveChangesAsync();
    }

    return Results.Ok(new { IsFavorite = false });
}).RequireAuthorization();

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
    await SendToStaffAsync(db, $"{userName} solicitó {entity.RequestedSeats} asiento(s) para \"{trip.Title}\".");

    return Results.Created($"/api/trips/{id}/capacity-requests/{entity.Id}", entity);
}).RequireAuthorization();

app.MapGet("/api/trips/{id}/capacity-requests", async (int id, AppDbContext db) =>
    await db.CapacityRequests
        .Include(c => c.User)
        .Where(c => c.TripId == id)
        .OrderByDescending(c => c.CreatedAt)
        .ToListAsync()).RequireAuthorization("AdminOnly");

app.MapGet("/api/trips/{id}/bookings", async (int id, AppDbContext db, string? status) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var query = db.Bookings
        .Include(b => b.User)
        .Include(b => b.Payments)
        .Include(b => b.Passengers)
        .Where(b => b.TripId == id);

    if (TryParseBookingStatus(status, out var parsed))
        query = query.Where(b => b.Status == parsed);

    return Results.Ok(await query.OrderByDescending(b => b.BookingDate).ToListAsync());
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

app.MapPut("/api/bookings/{id}/checkin", async (int id, UpdateBookingCheckinRequest request, AppDbContext db) =>
{
    var booking = await db.Bookings.Include(b => b.Trip).FirstOrDefaultAsync(b => b.Id == id);
    if (booking is null) return Results.NotFound("Reserva no encontrada.");

    if (booking.Status == BookingStatus.Cancelled)
        return Results.BadRequest("No se puede marcar el check-in de una reserva cancelada.");

    if (request.CheckedIn)
    {
        booking.CheckedIn = true;
        booking.CheckedInAt ??= DateTime.UtcNow;
    }
    else
    {
        booking.CheckedIn = false;
        booking.CheckedInAt = null;
    }

    await db.SaveChangesAsync();
    return Results.Ok(booking);
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/passengers/{id}/checkin", async (int id, UpdatePassengerCheckinRequest request, AppDbContext db) =>
{
    var passenger = await db.TripPassengers
        .Include(p => p.Booking)
        .FirstOrDefaultAsync(p => p.Id == id);
    if (passenger is null) return Results.NotFound("Pasajero no encontrado.");

    if (passenger.Booking is not null && passenger.Booking.Status == BookingStatus.Cancelled)
        return Results.BadRequest("No se puede marcar el check-in de una reserva cancelada.");

    if (request.CheckedIn)
    {
        passenger.CheckedIn = true;
        passenger.CheckedInAt ??= DateTime.UtcNow;
    }
    else
    {
        passenger.CheckedIn = false;
        passenger.CheckedInAt = null;
    }

    await db.SaveChangesAsync();
    return Results.Ok(passenger);
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/checkin/token", async (CheckinByTokenRequest request, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.QrToken))
        return Results.BadRequest("El código QR es inválido.");

    var token = request.QrToken.Trim();

    var trip = await db.Trips.FindAsync(request.TripId);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");
    if (trip.DepartureCompleted)
        return Results.BadRequest("La salida ya fue completada, el check-in está cerrado.");
    if (!trip.CheckInOpen)
        return Results.BadRequest("El check-in está cerrado.");

    var passenger = await db.TripPassengers
        .Include(p => p.Booking)
            .ThenInclude(b => b.Trip)
        .Include(p => p.Booking)
            .ThenInclude(b => b.User)
        .FirstOrDefaultAsync(p => p.QrToken == token);

    if (passenger is not null)
    {
        var booking = passenger.Booking;
        if (booking is null || booking.TripId != request.TripId)
            return Results.BadRequest("Este código pertenece a otro viaje.");
        if (booking.Status == BookingStatus.Cancelled)
            return Results.BadRequest("La reserva de este pasajero está cancelada.");

        if (passenger.CheckedIn)
        {
            return Results.Ok(new CheckinTokenResult("passenger", passenger.Name, passenger.CheckedInAt, true, null));
        }

        passenger.CheckedIn = true;
        passenger.CheckedInAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new CheckinTokenResult("passenger", passenger.Name, passenger.CheckedInAt, false, null));
    }

    var foundBooking = await db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.User)
        .FirstOrDefaultAsync(b => b.QrToken == token);

    if (foundBooking is not null)
    {
        if (foundBooking.TripId != request.TripId)
            return Results.BadRequest("Este código pertenece a otro viaje.");
        if (foundBooking.Status == BookingStatus.Cancelled)
            return Results.BadRequest("La reserva está cancelada.");

        if (foundBooking.CheckedIn)
        {
            return Results.Ok(new CheckinTokenResult("booking", foundBooking.User?.Name ?? $"Usuario #{foundBooking.UserId}", foundBooking.CheckedInAt, true, foundBooking.NumberOfSeats));
        }

        foundBooking.CheckedIn = true;
        foundBooking.CheckedInAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new CheckinTokenResult("booking", foundBooking.User?.Name ?? $"Usuario #{foundBooking.UserId}", foundBooking.CheckedInAt, false, foundBooking.NumberOfSeats));
    }

    return Results.NotFound("Código QR no reconocido.");
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/trips/{id}/departure", async (int id, UpdateDepartureRequest request, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    if (request.DepartureCompleted is true)
    {
        trip.DepartureCompleted = true;
        trip.CheckInOpen = false;
    }
    else if (request.DepartureCompleted is false)
    {
        trip.DepartureCompleted = false;
    }

    if (request.CheckInOpen is true && !trip.DepartureCompleted)
        trip.CheckInOpen = true;
    else if (request.CheckInOpen is false)
        trip.CheckInOpen = false;

    await db.SaveChangesAsync();
    return Results.Ok(trip);
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/trips/{id}/finalize", async (int id, UpdateFinalizeRequest request, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    trip.Finalized = request.Finalized;
    await db.SaveChangesAsync();

    if (request.Finalized)
    {
        var userIds = await db.Bookings
            .Where(b => b.TripId == id && b.Status != BookingStatus.Cancelled)
            .Select(b => b.UserId)
            .Distinct()
            .ToListAsync();

        if (userIds.Count > 0)
        {
            foreach (var userId in userIds)
            {
                db.Notifications.Add(new UserNotification
                {
                    UserId = userId,
                    Message = $"El viaje \"{trip.Title}\" ha finalizado. ¡Califica tu experiencia en Mis Viajes!",
                    CreatedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }
    }

    return Results.Ok(trip);
}).RequireAuthorization("StaffOnly");

app.MapGet("/api/trips/{id}/ratings", async (int id, AppDbContext db) =>
    await db.TripRatings
        .Where(r => r.TripId == id)
        .OrderByDescending(r => r.CreatedAt)
        .ToListAsync()).RequireAuthorization("StaffOnly");

app.MapGet("/api/trips/{id}/rating/me", async (int id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var rating = await db.TripRatings
        .FirstOrDefaultAsync(r => r.TripId == id && r.UserId == userId);
    return rating is null ? Results.Ok(null) : Results.Ok(rating);
}).RequireAuthorization();

app.MapPut("/api/trips/{id}/rating", async (int id, UpdateTripRatingRequest request, AppDbContext db, ClaimsPrincipal principal) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    if (!trip.Finalized)
        return Results.BadRequest("El viaje aún no ha finalizado; la calificación se habilitará cuando se cierre.");

    if (request.Rating is < 1 or > 5)
        return Results.BadRequest("Indica una calificación del 1 al 5.");

    var userId = GetUserId(principal);
    var user = await db.Users.FindAsync(userId);
    if (user is null) return Results.Forbid();

    var rating = await db.TripRatings
        .FirstOrDefaultAsync(r => r.TripId == id && r.UserId == userId);

    if (rating is null)
    {
        rating = new TripRating
        {
            TripId = id,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        db.TripRatings.Add(rating);
    }

    rating.Rating = request.Rating;
    rating.Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment!.Trim();
    rating.UserName = user.Name ?? user.Email;
    rating.Destination = trip.Destination;
    rating.TripDate = trip.StartDate;
    rating.TransportType = trip.TransportType;
    rating.Price = trip.Price;

    await db.SaveChangesAsync();
    return Results.Ok(rating);
}).RequireAuthorization();

app.MapGet("/api/admin/auditlog", async (AppDbContext db, string? entity, int? id, int? limit) =>
{
    var query = db.AuditLogs.AsNoTracking().AsQueryable();

    if (!string.IsNullOrWhiteSpace(entity))
        query = query.Where(a => a.EntityType == entity);

    if (id.HasValue)
        query = query.Where(a => a.EntityId == id.Value);

    var items = await query
        .OrderByDescending(a => a.CreatedAt)
        .ThenByDescending(a => a.Id)
        .Take(Math.Clamp(limit ?? 200, 1, 1000))
        .ToListAsync();

    return Results.Ok(items);
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/admin/stats", async (AppDbContext db) =>
{
    var now = DateTime.UtcNow;
    var trips = await db.Trips.Include(t => t.Bookings).ToListAsync();

    var activeBookings = trips
        .SelectMany(t => t.Bookings)
        .Where(b => b.Status != BookingStatus.Cancelled)
        .ToList();
    var seatsSold = activeBookings.Sum(b => b.NumberOfSeats);
    var capacityTotal = trips.Sum(t => t.Capacity);

    var payments = await db.Payments
        .Where(p => p.Status == PaymentStatus.Completed)
        .ToListAsync();

    var stats = new AdminStats
    {
        TripsCount = trips.Count,
        BookingsCount = activeBookings.Count,
        SeatsSold = seatsSold,
        RevenueTotal = Math.Round(payments.Sum(p => p.Amount), 2),
        OccupancyPercent = capacityTotal > 0 ? Math.Round(seatsSold * 100.0 / capacityTotal, 1) : 0,
        MonthlyRevenue = payments
            .Where(p => p.PaymentDate >= now.AddMonths(-11))
            .GroupBy(p => new DateTime(p.PaymentDate.Year, p.PaymentDate.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new MonthlyRevenue
            {
                Month = g.Key.ToString("yyyy-MM"),
                Amount = Math.Round(g.Sum(p => p.Amount), 2)
            })
            .ToList(),
        OccupancyByTrip = trips
            .Select(t =>
            {
                var sold = t.Bookings.Where(b => b.Status != BookingStatus.Cancelled).Sum(b => b.NumberOfSeats);
                return new TripOccupancy
                {
                    TripId = t.Id,
                    Title = t.Title ?? "",
                    Capacity = t.Capacity,
                    Sold = sold,
                    Percent = t.Capacity > 0 ? Math.Round(sold * 100.0 / t.Capacity, 1) : 0
                };
            })
            .OrderByDescending(o => o.Percent)
            .ToList(),
        TopDestinations = trips
            .GroupBy(t => t.Destination ?? "Sin destino")
            .Select(g => new TopDestination
            {
                Destination = g.Key,
                Seats = g.Sum(t => t.Bookings.Where(b => b.Status != BookingStatus.Cancelled).Sum(b => b.NumberOfSeats))
            })
            .OrderByDescending(d => d.Seats)
            .Take(5)
            .ToList()
    };

    return Results.Ok(stats);
}).RequireAuthorization("AdminOnly");

app.Run();

static async Task RecomputeAvailabilityAsync(AppDbContext db, Trip trip)
{
    var sold = await db.Bookings
        .Where(b => b.TripId == trip.Id && b.Status != BookingStatus.Cancelled)
        .SumAsync(b => (int?)b.NumberOfSeats) ?? 0;
    trip.AvailableSeats = trip.Capacity - sold;

    // Recompute option availability if options exist
    if (trip.HasOptions)
    {
        var options = await db.TripOptions.Where(o => o.TripId == trip.Id).ToListAsync();
        foreach (var opt in options)
        {
            var soldOpt = await db.BookingItems
                .Where(i => i.TripOptionId == opt.Id)
                .Join(db.Bookings.Where(b => b.Status != BookingStatus.Cancelled), i => i.BookingId, b => b.Id, (i,b) => i)
                .SumAsync(i => (int?)(i.Adults + i.Children)) ?? 0;
            opt.AvailableSeats = opt.Capacity - soldOpt;
        }
    }
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

static async Task SendToStaffAsync(AppDbContext db, string message)
{
    var adminIds = await db.Users
        .Where(u => u.Role == UserRole.Admin || u.Role == UserRole.Coordinador)
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

static bool IsChildAge(int? age) => age is >= 0 and <= 11;

static string? NormalizeCategory(string? category)
{
    var trimmed = category?.Trim();
    return string.IsNullOrEmpty(trimmed) ? null : trimmed;
}

static List<TripSeatRow> BuildSeatRows(int capacity, Dictionary<int, TripSeat> assigned)
{
    const int leftSeats = 2;
    const int rightSeats = 2;
    const int aisleSlot = leftSeats;
    const int slotsPerRow = leftSeats + 1 + rightSeats;
    const int seatsPerRow = leftSeats + rightSeats;
    var rows = new List<TripSeatRow>();
    if (capacity <= 0) return rows;

    var totalRows = (int)Math.Ceiling(capacity / (double)seatsPerRow);
    for (var rowIndex = 0; rowIndex < totalRows; rowIndex++)
    {
        var row = new TripSeatRow { RowNumber = rowIndex + 1 };
        var seatIndex = rowIndex * seatsPerRow;

        for (var slot = 0; slot < slotsPerRow; slot++)
        {
            if (slot == aisleSlot)
            {
                row.Seats.Add(new TripSeat { IsAisle = true });
                continue;
            }

            seatIndex++;
            if (seatIndex > capacity) break;

            if (assigned.TryGetValue(seatIndex, out var seat))
            {
                row.Seats.Add(seat);
            }
            else
            {
                row.Seats.Add(new TripSeat { Number = seatIndex });
            }
        }
        rows.Add(row);
    }

    return rows;
}

static async Task<Dictionary<int, TripSeat>> GetOccupiedSeatsAsync(AppDbContext db, int tripId, int capacity, int? ignoreBookingId = null)
{
    var assigned = new Dictionary<int, TripSeat>();
    if (capacity <= 0) return assigned;

    var passengers = await db.TripPassengers
        .Where(p => p.Booking != null
            && p.Booking.TripId == tripId
            && p.Booking.Status != BookingStatus.Cancelled
            && p.SeatNumber != null)
        .ToListAsync();

    foreach (var p in passengers)
    {
        var number = p.SeatNumber!.Value;
        if (number < 1 || number > capacity || assigned.ContainsKey(number)) continue;
        assigned[number] = new TripSeat
        {
            Number = number,
            IsOccupied = true,
            IsAssigned = true,
            OccupiedBy = p.Name,
            BookingId = p.BookingId,
            CheckedIn = p.CheckedIn
        };
    }

    var bookings = await db.Bookings
        .Where(b => b.TripId == tripId && b.Status != BookingStatus.Cancelled)
        .OrderBy(b => b.Id)
        .Select(b => new { b.Id, b.NumberOfSeats, b.SeatNumber })
        .ToListAsync();

    var assignedPerBooking = passengers
        .GroupBy(p => p.BookingId)
        .ToDictionary(g => g.Key, g => g.Count());

    foreach (var booking in bookings)
    {
        if (booking.SeatNumber is int holderSeat
            && holderSeat >= 1 && holderSeat <= capacity && !assigned.ContainsKey(holderSeat))
        {
            assigned[holderSeat] = new TripSeat
            {
                Number = holderSeat,
                IsOccupied = true,
                IsAssigned = true,
                OccupiedBy = "Titular de la reserva",
                BookingId = booking.Id
            };
        }

        if (ignoreBookingId == booking.Id) continue;

        var assignedCount = assignedPerBooking.TryGetValue(booking.Id, out var count) ? count : 0;
        var missing = booking.NumberOfSeats - assignedCount - (booking.SeatNumber.HasValue ? 1 : 0);
        for (var i = 0; i < missing; i++)
        {
            var free = Enumerable.Range(1, capacity).FirstOrDefault(n => !assigned.ContainsKey(n));
            if (free < 1) break;
            assigned[free] = new TripSeat
            {
                Number = free,
                IsOccupied = true,
                IsAssigned = false,
                OccupiedBy = "Reserva sin pasajero registrado",
                BookingId = booking.Id
            };
        }
    }

    return assigned;
}

static async Task<HashSet<int>> GetTakenSeatNumbersAsync(AppDbContext db, Trip trip, int? ignoreBookingId = null)
{
    var occupied = await GetOccupiedSeatsAsync(db, trip.Id, Math.Max(0, trip.Capacity), ignoreBookingId);
    return new HashSet<int>(occupied.Keys);
}

static async Task AssignFreeSeatsAsync(AppDbContext db, Trip trip, IReadOnlyList<TripPassenger> passengers, int? bookingId = null)
{
    var pending = passengers.Where(p => !p.SeatNumber.HasValue).ToList();
    if (pending.Count == 0 || trip.Capacity <= 0) return;

    var taken = await GetTakenSeatNumbersAsync(db, trip, bookingId);

    var free = Enumerable.Range(1, trip.Capacity)
        .Where(n => !taken.Contains(n))
        .Take(pending.Count)
        .ToList();

    for (var i = 0; i < pending.Count && i < free.Count; i++)
    {
        pending[i].SeatNumber = free[i];
    }
}

static decimal ComputeBookingTotal(Trip trip, IEnumerable<TripPassenger> passengers, IEnumerable<BookingItem>? items = null)
{
    if (items != null && items.Any())
    {
        decimal total = 0;
        foreach (var it in items)
            total += it.LineTotal;
        return Math.Round(total, 2);
    }
    var totalLegacy = trip.Price;
    foreach (var p in passengers)
    {
        totalLegacy += p.IsChild && trip.ChildPrice.HasValue ? trip.ChildPrice.Value : trip.Price;
    }
    return Math.Round(totalLegacy, 2);
}

static bool TryParseBookingStatus(string? status, out BookingStatus parsed)
{
    parsed = default;
    if (string.IsNullOrWhiteSpace(status))
        return false;
    if (status.Equals("All", StringComparison.OrdinalIgnoreCase))
        return false;
    return Enum.TryParse(status, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
}

static string GenerateTemporaryPassword()
{
    const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    return RandomNumberGenerator.GetString(alphabet, 10);
}

static string GenerateQrToken()
{
    var bytes = RandomNumberGenerator.GetBytes(24);
    return Convert.ToBase64String(bytes)
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');
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

static async Task EnsureTripHasOptionsColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Trips", "HasOptions", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? @"ALTER TABLE ""Trips"" ADD COLUMN ""HasOptions"" INTEGER NOT NULL DEFAULT 0;"
            : @"ALTER TABLE ""Trips"" ADD COLUMN ""HasOptions"" boolean NOT NULL DEFAULT false;");
    }
}


static async Task EnsureTripOptionsTableAsync(AppDbContext db, string provider)
{
    if (provider == "sqlite")
    {
        await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""TripOptions"" (
              ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_TripOptions"" PRIMARY KEY AUTOINCREMENT,
              ""TripId"" INTEGER NOT NULL,
              ""Name"" TEXT NOT NULL,
              ""Description"" TEXT NULL,
              ""PriceAdult"" TEXT NOT NULL,
              ""PriceChild"" TEXT NULL,
              ""Capacity"" INTEGER NOT NULL DEFAULT 0,
              ""AvailableSeats"" INTEGER NOT NULL DEFAULT 0,
              ""Benefits"" TEXT NULL,
              ""IsActive"" INTEGER NOT NULL DEFAULT 1,
              ""Order"" INTEGER NOT NULL DEFAULT 1,
              ""CapacityMode"" INTEGER NOT NULL DEFAULT 0,
              ""CreatedAt"" TEXT NOT NULL,
              CONSTRAINT ""FK_TripOptions_Trips_TripId"" FOREIGN KEY (""TripId"") REFERENCES ""Trips"" (""Id"") ON DELETE RESTRICT
          );");
        await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_TripOptions_TripId"" ON ""TripOptions"" (""TripId"");");
        return;
    }

    await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""TripOptions"" (
          ""Id"" integer GENERATED BY DEFAULT AS IDENTITY NOT NULL,
          ""TripId"" integer NOT NULL,
          ""Name"" character varying(80) NOT NULL,
          ""Description"" character varying(400) NULL,
          ""PriceAdult"" numeric(18,2) NOT NULL,
          ""PriceChild"" numeric(18,2) NULL,
          ""Capacity"" integer NOT NULL DEFAULT 0,
          ""AvailableSeats"" integer NOT NULL DEFAULT 0,
          ""Benefits"" text NULL,
          ""IsActive"" boolean NOT NULL DEFAULT true,
          ""Order"" integer NOT NULL DEFAULT 1,
          ""CapacityMode"" integer NOT NULL DEFAULT 0,
          ""CreatedAt"" timestamp without time zone NOT NULL,
          CONSTRAINT ""PK_TripOptions"" PRIMARY KEY (""Id""),
          CONSTRAINT ""FK_TripOptions_Trips_TripId"" FOREIGN KEY (""TripId"") REFERENCES ""Trips"" (""Id"") ON DELETE RESTRICT
      );");
    await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_TripOptions_TripId"" ON ""TripOptions"" (""TripId"");");
}


static async Task EnsureBookingItemsTableAsync(AppDbContext db, string provider)
{
    if (provider == "sqlite")
    {
        await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""BookingItems"" (
              ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_BookingItems"" PRIMARY KEY AUTOINCREMENT,
              ""BookingId"" INTEGER NOT NULL,
              ""TripOptionId"" INTEGER NOT NULL,
              ""Adults"" INTEGER NOT NULL DEFAULT 0,
              ""Children"" INTEGER NOT NULL DEFAULT 0,
              ""UnitPriceAdult"" TEXT NOT NULL,
              ""UnitPriceChild"" TEXT NULL,
              ""OptionName"" TEXT NULL,
              CONSTRAINT ""FK_BookingItems_Bookings_BookingId"" FOREIGN KEY (""BookingId"") REFERENCES ""Bookings"" (""Id"") ON DELETE CASCADE,
              CONSTRAINT ""FK_BookingItems_TripOptions_TripOptionId"" FOREIGN KEY (""TripOptionId"") REFERENCES ""TripOptions"" (""Id"") ON DELETE RESTRICT
          );");
        await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_BookingItems_BookingId"" ON ""BookingItems"" (""BookingId"");");
        await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_BookingItems_TripOptionId"" ON ""BookingItems"" (""TripOptionId"");");
        return;
    }

    await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""BookingItems"" (
          ""Id"" integer GENERATED BY DEFAULT AS IDENTITY NOT NULL,
          ""BookingId"" integer NOT NULL,
          ""TripOptionId"" integer NOT NULL,
          ""Adults"" integer NOT NULL DEFAULT 0,
          ""Children"" integer NOT NULL DEFAULT 0,
          ""UnitPriceAdult"" numeric(18,2) NOT NULL,
          ""UnitPriceChild"" numeric(18,2) NULL,
          ""OptionName"" character varying(80) NULL,
          CONSTRAINT ""PK_BookingItems"" PRIMARY KEY (""Id""),
          CONSTRAINT ""FK_BookingItems_Bookings_BookingId"" FOREIGN KEY (""BookingId"") REFERENCES ""Bookings"" (""Id"") ON DELETE CASCADE,
          CONSTRAINT ""FK_BookingItems_TripOptions_TripOptionId"" FOREIGN KEY (""TripOptionId"") REFERENCES ""TripOptions"" (""Id"") ON DELETE RESTRICT
      );");
    await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_BookingItems_BookingId"" ON ""BookingItems"" (""BookingId"");");
    await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_BookingItems_TripOptionId"" ON ""BookingItems"" (""TripOptionId"");");
}


static async Task EnsureDefaultTripOptionsBackfillAsync(AppDbContext db, string provider)
{
    try
    {
        var trips = await db.Trips
            .Where(t => !t.HasOptions && !db.TripOptions.Any(o => o.TripId == t.Id))
            .Select(t => new { t.Id, t.Price, t.ChildPrice, t.Capacity, t.AvailableSeats })
            .ToListAsync();
        if (trips.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var t in trips)
        {
            db.TripOptions.Add(new TravelAgency.Shared.Models.TripOption
            {
                TripId = t.Id,
                Name = "General",
                Description = null,
                PriceAdult = t.Price,
                PriceChild = t.ChildPrice,
                Capacity = t.Capacity,
                AvailableSeats = t.AvailableSeats,
                Benefits = null,
                IsActive = true,
                Order = 1,
                CapacityMode = TravelAgency.Shared.Models.TripOptionCapacityMode.Shared,
                CreatedAt = now
            });
            var trip = await db.Trips.FirstAsync(x => x.Id == t.Id);
            trip.HasOptions = true;
        }
        await db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"EnsureDefaultTripOptionsBackfillAsync warning: {ex.Message}");
    }
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

static async Task EnsureCheckinColumnsAsync(AppDbContext db, string provider)
{
    var tripColumns = new[] { "CheckInOpen", "DepartureCompleted" };
    foreach (var column in tripColumns)
    {
        if (!await ColumnExistsAsync(db, "Trips", column, provider))
        {
            await TryExecAsync(db, provider == "sqlite"
                ? $"ALTER TABLE \"Trips\" ADD COLUMN \"{column}\" INTEGER NOT NULL DEFAULT 0;"
                : $"ALTER TABLE \"Trips\" ADD COLUMN \"{column}\" boolean NOT NULL DEFAULT false;");
        }
    }

    if (!await ColumnExistsAsync(db, "Bookings", "CheckedIn", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Bookings\" ADD COLUMN \"CheckedIn\" INTEGER NOT NULL DEFAULT 0;"
            : "ALTER TABLE \"Bookings\" ADD COLUMN \"CheckedIn\" boolean NOT NULL DEFAULT false;");
    }

    if (!await ColumnExistsAsync(db, "Bookings", "CheckedInAt", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Bookings\" ADD COLUMN \"CheckedInAt\" TEXT NULL;"
            : "ALTER TABLE \"Bookings\" ADD COLUMN \"CheckedInAt\" timestamp without time zone NULL;");
    }
}

static async Task EnsureBookingQrColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Bookings", "QrToken", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Bookings\" ADD COLUMN \"QrToken\" TEXT NULL;"
            : "ALTER TABLE \"Bookings\" ADD COLUMN \"QrToken\" character varying(64) NULL;");
    }

    var missing = await db.Bookings
        .Where(b => b.QrToken == null || b.QrToken == "")
        .ToListAsync();
    if (missing.Count == 0) return;

    var used = new HashSet<string>(await db.Bookings
        .Where(b => b.QrToken != null)
        .Select(b => b.QrToken!)
        .ToListAsync(), StringComparer.Ordinal);

    foreach (var booking in missing)
    {
        string token;
        do { token = GenerateQrToken(); } while (!used.Add(token));
        booking.QrToken = token;
    }

    await db.SaveChangesAsync();
}

static async Task EnsureUserQrColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Users", "QrToken", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Users\" ADD COLUMN \"QrToken\" TEXT NULL;"
            : "ALTER TABLE \"Users\" ADD COLUMN \"QrToken\" character varying(64) NULL;");
    }

var missing = await db.Users
        .Where(u => u.QrToken == null || u.QrToken == "")
        .Select(u => u.Id)
        .ToListAsync();

    if (missing.Count > 0)
    {
        var used = new HashSet<string>(await db.Users
            .Where(u => u.QrToken != null)
            .Select(u => u.QrToken!)
            .ToListAsync(), StringComparer.Ordinal);

        foreach (var userId in missing)
        {
            string token;
            do { token = GenerateQrToken(); } while (!used.Add(token));
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Users\" SET \"QrToken\" = {token} WHERE \"Id\" = {userId}");
        }
    }

    await TryExecAsync(db, provider == "sqlite"
        ? "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Users_QrToken\" ON \"Users\" (\"QrToken\");"
        : "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Users_QrToken\" ON \"Users\" (\"QrToken\");");
}

static async Task EnsureUserEmergencyContactColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Users", "EmergencyContact", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Users\" ADD COLUMN \"EmergencyContact\" TEXT NULL;"
            : "ALTER TABLE \"Users\" ADD COLUMN \"EmergencyContact\" character varying(120) NULL;");
    }
}

static async Task EnsurePassengersTableAsync(AppDbContext db, string provider)
{
    await TryExecAsync(db, provider == "sqlite"
        ? """
          CREATE TABLE IF NOT EXISTS "TripPassengers" (
              "Id" INTEGER NOT NULL CONSTRAINT "PK_TripPassengers" PRIMARY KEY AUTOINCREMENT,
              "BookingId" INTEGER NOT NULL,
              "Name" TEXT NULL,
              "QrToken" TEXT NULL,
              "CheckedIn" INTEGER NOT NULL DEFAULT 0,
              "CheckedInAt" TEXT NULL,
              CONSTRAINT "FK_TripPassengers_Bookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "Bookings" ("Id") ON DELETE CASCADE
          );
          """
        : """
          CREATE TABLE IF NOT EXISTS "TripPassengers" (
              "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
              "BookingId" integer NOT NULL,
              "Name" character varying(120) NULL,
              "QrToken" character varying(64) NULL,
              "CheckedIn" boolean NOT NULL DEFAULT false,
              "CheckedInAt" timestamp without time zone NULL,
              CONSTRAINT "FK_TripPassengers_Bookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "Bookings" ("Id") ON DELETE CASCADE
          );
          """);

    await TryExecAsync(db, provider == "sqlite"
        ? "CREATE INDEX IF NOT EXISTS \"IX_TripPassengers_BookingId\" ON \"TripPassengers\" (\"BookingId\");"
        : "CREATE INDEX IF NOT EXISTS \"IX_TripPassengers_BookingId\" ON \"TripPassengers\" (\"BookingId\");");
}

static async Task EnsureTripFinalizedColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Trips", "Finalized", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Trips\" ADD COLUMN \"Finalized\" INTEGER NOT NULL DEFAULT 0;"
            : "ALTER TABLE \"Trips\" ADD COLUMN \"Finalized\" boolean NOT NULL DEFAULT false;");
    }
}

static async Task EnsureBookingDeadlineColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Trips", "BookingDeadline", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Trips\" ADD COLUMN \"BookingDeadline\" TEXT NULL;"
            : "ALTER TABLE \"Trips\" ADD COLUMN \"BookingDeadline\" timestamp NULL;");
    }
}

static async Task EnsureTripCategoryColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Trips", "Category", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Trips\" ADD COLUMN \"Category\" TEXT NULL;"
            : "ALTER TABLE \"Trips\" ADD COLUMN \"Category\" varchar(60) NULL;");
    }
}

static async Task EnsureRatingsTableAsync(AppDbContext db, string provider)
{
    await TryExecAsync(db, provider == "sqlite"
        ? """
          CREATE TABLE IF NOT EXISTS "TripRatings" (
              "Id" INTEGER NOT NULL CONSTRAINT "PK_TripRatings" PRIMARY KEY AUTOINCREMENT,
              "TripId" INTEGER NOT NULL,
              "UserId" INTEGER NOT NULL,
              "Rating" INTEGER NOT NULL,
              "Comment" TEXT NULL,
              "UserName" TEXT NULL,
              "Destination" TEXT NULL,
              "TripDate" TEXT NOT NULL,
              "TransportType" INTEGER NOT NULL,
              "Price" TEXT NOT NULL,
              "CreatedAt" TEXT NOT NULL,
              CONSTRAINT "FK_TripRatings_Trips_TripId" FOREIGN KEY ("TripId") REFERENCES "Trips" ("Id") ON DELETE CASCADE,
              CONSTRAINT "FK_TripRatings_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
          );
          """
        : """
          CREATE TABLE IF NOT EXISTS "TripRatings" (
              "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
              "TripId" integer NOT NULL,
              "UserId" integer NOT NULL,
              "Rating" integer NOT NULL,
              "Comment" text NULL,
              "UserName" character varying(120) NULL,
              "Destination" character varying(150) NULL,
              "TripDate" timestamp without time zone NOT NULL,
              "TransportType" integer NOT NULL,
              "Price" numeric(18,2) NOT NULL,
              "CreatedAt" timestamp without time zone NOT NULL,
              CONSTRAINT "FK_TripRatings_Trips_TripId" FOREIGN KEY ("TripId") REFERENCES "Trips" ("Id") ON DELETE CASCADE,
              CONSTRAINT "FK_TripRatings_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
          );
          """);

    await TryExecAsync(db, provider == "sqlite"
        ? "CREATE INDEX IF NOT EXISTS \"IX_TripRatings_TripId\" ON \"TripRatings\" (\"TripId\");"
        : "CREATE INDEX IF NOT EXISTS \"IX_TripRatings_TripId\" ON \"TripRatings\" (\"TripId\");");

    await TryExecAsync(db, provider == "sqlite"
        ? "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TripRatings_UserId_TripId\" ON \"TripRatings\" (\"UserId\", \"TripId\");"
        : "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TripRatings_UserId_TripId\" ON \"TripRatings\" (\"UserId\", \"TripId\");");
}

static async Task EnsureChildPriceColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Trips", "ChildPrice", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Trips\" ADD COLUMN \"ChildPrice\" TEXT NULL;"
            : "ALTER TABLE \"Trips\" ADD COLUMN \"ChildPrice\" numeric(18,2) NULL;");
    }
}

static async Task EnsurePassengerAgeColumnsAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "TripPassengers", "Age", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"TripPassengers\" ADD COLUMN \"Age\" INTEGER NULL;"
            : "ALTER TABLE \"TripPassengers\" ADD COLUMN \"Age\" integer NULL;");
    }
    if (!await ColumnExistsAsync(db, "TripPassengers", "IsChild", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"TripPassengers\" ADD COLUMN \"IsChild\" INTEGER NOT NULL DEFAULT 0;"
            : "ALTER TABLE \"TripPassengers\" ADD COLUMN \"IsChild\" boolean NOT NULL DEFAULT false;");
    }
}

static async Task EnsurePassengerSeatColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "TripPassengers", "SeatNumber", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"TripPassengers\" ADD COLUMN \"SeatNumber\" INTEGER NULL;"
            : "ALTER TABLE \"TripPassengers\" ADD COLUMN \"SeatNumber\" integer NULL;");
    }
}

static async Task EnsureBookingSeatColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Bookings", "SeatNumber", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Bookings\" ADD COLUMN \"SeatNumber\" INTEGER NULL;"
            : "ALTER TABLE \"Bookings\" ADD COLUMN \"SeatNumber\" integer NULL;");
    }
}

static async Task EnsureCancellationPolicyColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Trips", "CancellationDaysLimit", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Trips\" ADD COLUMN \"CancellationDaysLimit\" INTEGER NULL;"
            : "ALTER TABLE \"Trips\" ADD COLUMN \"CancellationDaysLimit\" integer NULL;");
    }

    // Todo viaje necesita limite de cancelacion para que el cliente siempre pueda
    // cancelar; los que quedaron sin valor reciben el limite por defecto.
    await TryExecAsync(db, provider == "sqlite"
        ? $"UPDATE \"Trips\" SET \"CancellationDaysLimit\" = {BookingRefundPolicy.DefaultCancellationDaysLimit} WHERE \"CancellationDaysLimit\" IS NULL;"
        : $"UPDATE \"Trips\" SET \"CancellationDaysLimit\" = {BookingRefundPolicy.DefaultCancellationDaysLimit} WHERE \"CancellationDaysLimit\" IS NULL;");
}

/// Registra quien cancelo una reserva, para poder distinguir la cancelacion
/// hecha por el cliente desde la app de la hecha por la agencia.
static async Task EnsureBookingCancellationAuditColumnsAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Bookings", "CancelledAt", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Bookings\" ADD COLUMN \"CancelledAt\" TEXT NULL;"
            : "ALTER TABLE \"Bookings\" ADD COLUMN \"CancelledAt\" timestamptz NULL;");
    }

    if (!await ColumnExistsAsync(db, "Bookings", "CancelledByUserId", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Bookings\" ADD COLUMN \"CancelledByUserId\" INTEGER NULL;"
            : "ALTER TABLE \"Bookings\" ADD COLUMN \"CancelledByUserId\" integer NULL;");
    }
}

/// Permite reembolsos parciales: la multa de cancelacion no se devuelve,
/// asi que el pago queda marcado como Refunded con un monto menor a Amount.
static async Task EnsurePaymentRefundedAmountColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Payments", "RefundedAmount", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Payments\" ADD COLUMN \"RefundedAmount\" TEXT NOT NULL DEFAULT 0;"
            : "ALTER TABLE \"Payments\" ADD COLUMN \"RefundedAmount\" numeric(18,2) NOT NULL DEFAULT 0;");
    }
}

static async Task EnsureFavoriteTripsTableAsync(AppDbContext db, string provider)
{
    await TryExecAsync(db, provider == "sqlite"
        ? """
          CREATE TABLE IF NOT EXISTS "FavoriteTrips" (
              "Id" INTEGER NOT NULL CONSTRAINT "PK_FavoriteTrips" PRIMARY KEY AUTOINCREMENT,
              "UserId" INTEGER NOT NULL,
              "TripId" INTEGER NOT NULL,
              "CreatedAt" TEXT NOT NULL,
              CONSTRAINT "FK_FavoriteTrips_Trips_TripId" FOREIGN KEY ("TripId") REFERENCES "Trips" ("Id") ON DELETE CASCADE,
              CONSTRAINT "FK_FavoriteTrips_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
          );
          """
        : """
          CREATE TABLE IF NOT EXISTS "FavoriteTrips" (
              "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
              "UserId" integer NOT NULL,
              "TripId" integer NOT NULL,
              "CreatedAt" timestamp with time zone NOT NULL,
              CONSTRAINT "FK_FavoriteTrips_Trips_TripId" FOREIGN KEY ("TripId") REFERENCES "Trips" ("Id") ON DELETE CASCADE,
              CONSTRAINT "FK_FavoriteTrips_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
          );
          """);

    await TryExecAsync(db, provider == "sqlite"
        ? "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_FavoriteTrips_UserId_TripId\" ON \"FavoriteTrips\" (\"UserId\", \"TripId\");"
        : "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_FavoriteTrips_UserId_TripId\" ON \"FavoriteTrips\" (\"UserId\", \"TripId\");");
}

static async Task EnsureAuditLogTableAsync(AppDbContext db, string provider)
{
    await TryExecAsync(db, provider == "sqlite"
        ? """
          CREATE TABLE IF NOT EXISTS "AuditLogs" (
              "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY AUTOINCREMENT,
              "UserId" INTEGER NOT NULL,
              "UserName" TEXT NULL,
              "EntityType" TEXT NOT NULL,
              "EntityId" INTEGER NOT NULL,
              "Action" TEXT NOT NULL,
              "Summary" TEXT NULL,
              "Details" TEXT NULL,
              "CreatedAt" TEXT NOT NULL
          );
          """
        : """
          CREATE TABLE IF NOT EXISTS "AuditLogs" (
              "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
              "UserId" integer NOT NULL,
              "UserName" character varying(120) NULL,
              "EntityType" character varying(30) NOT NULL,
              "EntityId" integer NOT NULL,
              "Action" character varying(20) NOT NULL,
              "Summary" character varying(1000) NULL,
              "Details" text NULL,
              "CreatedAt" timestamp without time zone NOT NULL
          );
          """);

    await TryExecAsync(db, provider == "sqlite"
        ? "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_Entity\" ON \"AuditLogs\" (\"EntityType\", \"EntityId\");"
        : "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_Entity\" ON \"AuditLogs\" (\"EntityType\", \"EntityId\");");

    await TryExecAsync(db, provider == "sqlite"
        ? "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_CreatedAt\" ON \"AuditLogs\" (\"CreatedAt\");"
        : "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_CreatedAt\" ON \"AuditLogs\" (\"CreatedAt\");");
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

record CheckinByTokenRequest(string? QrToken, int TripId);
record CheckinTokenResult(string Kind, string? Name, DateTime? CheckedInAt, bool AlreadyCheckedIn, int? Seats);
record UpdateBookingStatusRequest(BookingStatus Status);
record UpdateBookingCheckinRequest(bool CheckedIn);
record UpdatePassengerCheckinRequest(bool CheckedIn);
record PassengerInput(string? Name, int? Age, int? SeatNumber = null);
record CreateBookingRequest(int TripId, int NumberOfSeats, List<PassengerInput>? Passengers, int? SeatNumber = null, List<BookingOptionInput>? Options = null, bool? UseOptions = null);
record CreatePassengersRequest(List<PassengerInput>? Passengers);
record UpdateDepartureRequest(bool? CheckInOpen, bool? DepartureCompleted);
record UpdateFinalizeRequest(bool Finalized);
record UpdateTripRatingRequest(int Rating, string? Comment);
record DeleteTripRequest(string? Message);
record UpdateTripRouteRequest(double? OriginLatitude, double? OriginLongitude, double? DestinationLatitude, double? DestinationLongitude);
record UpsertPoiRequest(string? Name, string? Description, double Latitude, double Longitude);