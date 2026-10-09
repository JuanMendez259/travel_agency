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

// Lock in-process por usuario para las secciones de dinero del wallet. Asume una
// sola instancia de la API; un despliegue multi-instancia requeriria bloqueo a
// nivel base de datos o concurrencia optimista.
var walletLocks = new System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim>();

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
    await EnsureBookingDiscountColumnsAsync(db, provider);
    await EnsureBookingRefundsTableAsync(db, provider);
    await EnsureWalletTransactionsTableAsync(db, provider);
    await EnsureWalletTransactionPayoutColumnAsync(db, provider);
    await EnsurePayoutRequestsTableAsync(db, provider);
    await EnsureBookingSpecialNeedsColumnAsync(db, provider);
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
    await EnsureTripHotelColumnsAsync(db, provider);
    await EnsureTripFloorsColumnsAsync(db, provider);
    await EnsureTripOptionsTableAsync(db, provider);
    await EnsureTripOptionIsBaseColumnAsync(db, provider);
    await EnsureBookingItemsTableAsync(db, provider);
    await EnsureDiscountsTableAsync(db, provider);
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
        .Include(t => t.Options)
        .OrderByDescending(t => t.StartDate)
        .ToListAsync()).RequireAuthorization("StaffOnly");

app.MapGet("/api/trips/{id}", async (int id, AppDbContext db) =>
    await db.Trips
        .Include(t => t.PointsOfInterest)
        .Include(t => t.Options)
        .FirstOrDefaultAsync(t => t.Id == id) is Trip trip ? Results.Ok(trip) : Results.NotFound());

app.MapGet("/api/trips/{id:int}/seatmap", async (int id, AppDbContext db) =>
{
    var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var capacity = Math.Max(0, trip.Capacity);
    var assigned = await GetOccupiedSeatsAsync(db, id, capacity);
    var floor1 = trip.HasTwoFloors ? Math.Max(0, trip.Floor1Capacity ?? 0) : capacity;
    var floor2 = trip.HasTwoFloors ? Math.Max(0, trip.Floor2Capacity ?? 0) : 0;

    var map = new TripSeatMap
    {
        TripId = trip.Id,
        TripTitle = trip.Title,
        TransportType = trip.TransportType,
        Capacity = capacity,
        OccupiedCount = assigned.Count,
        HasTwoFloors = trip.HasTwoFloors,
        Floor1Capacity = trip.Floor1Capacity,
        Floor2Capacity = trip.Floor2Capacity
    };

    map.Rows.AddRange(BuildSeatRows(floor1, floor2, assigned));
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
    var floor1 = trip.HasTwoFloors ? Math.Max(0, trip.Floor1Capacity ?? 0) : capacity;
    var floor2 = trip.HasTwoFloors ? Math.Max(0, trip.Floor2Capacity ?? 0) : 0;

    var map = new TripSeatAvailability
    {
        TripId = trip.Id,
        TripTitle = trip.Title,
        TransportType = trip.TransportType,
        Capacity = capacity,
        OccupiedCount = occupied.Count,
        OccupiedSeats = occupied,
        HasTwoFloors = trip.HasTwoFloors,
        Floor1Capacity = trip.Floor1Capacity,
        Floor2Capacity = trip.Floor2Capacity
    };

    var seats = new Dictionary<int, TripSeat>();
    foreach (var number in occupied)
    {
        seats[number] = new TripSeat { Number = number, IsOccupied = true };
    }

    map.Rows.AddRange(BuildSeatRows(floor1, floor2, seats));
    return Results.Ok(map);
}).RequireAuthorization();

app.MapPost("/api/trips", async (Trip trip, AppDbContext db, ImageStorageService storage) =>
{
    if (trip.StartDate.Date < DateTime.UtcNow.Date)
        return Results.BadRequest("La fecha de salida no puede estar en el pasado.");
    if (trip.EndDate <= trip.StartDate)
        return Results.BadRequest("La fecha de fin no puede ser anterior (o igual) a la de salida.");

    if (NormalizeAndValidateFloors(trip) is { } floorsError)
        return Results.BadRequest(floorsError);

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

    if (ValidateHotelFields(trip) is { } hotelError)
        return Results.BadRequest(hotelError);

    trip.CreatedAt = DateTime.UtcNow;
    trip.Category = NormalizeCategory(trip.Category);
    trip.AvailableSeats = trip.BookableCapacity;

    // Sin imagen elegida se usa el placeholder compartido que ya existe en el
    // bucket, para que el viaje siempre tenga una portada consistente.
    if (string.IsNullOrWhiteSpace(trip.ImageUrl) && storage.PlaceholderUrl is not null)
        trip.ImageUrl = storage.PlaceholderUrl;

    db.Trips.Add(trip);
    await db.SaveChangesAsync();

    // Todo viaje nace con su entrada base "General" (se puede desactivar, no eliminar).
    db.TripOptions.Add(new TravelAgency.Shared.Models.TripOption
    {
        TripId = trip.Id,
        Name = "General",
        PriceAdult = trip.Price,
        PriceChild = trip.ChildPrice,
        Capacity = trip.BookableCapacity,
        AvailableSeats = trip.AvailableSeats,
        IsActive = true,
        Order = 1,
        CapacityMode = TravelAgency.Shared.Models.TripOptionCapacityMode.Shared,
        IsBase = true,
        CreatedAt = DateTime.UtcNow
    });
    trip.HasOptions = true;
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

    if (NormalizeAndValidateFloors(input) is { } floorsError)
        return Results.BadRequest(floorsError);

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

    if (ValidateHotelFields(input) is { } hotelError)
        return Results.BadRequest(hotelError);

    var capacityIncreased = input.Capacity > trip.Capacity;

    // PUT es reemplazo total: si el request no trae info de pisos (false + null) pero el
    // viaje actual tiene dos pisos y la capacidad total no cambió, conservar los pisos
    // (evita perderlos por un cliente que no conoce los campos nuevos).
    var inputHasFloorData = input.HasTwoFloors || input.Floor1Capacity is not null || input.Floor2Capacity is not null;
    var preserveFloors = !inputHasFloorData && trip.HasTwoFloors && input.Capacity == trip.Capacity;

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
    trip.IncludesHotel = input.IncludesHotel;
    trip.HotelName = string.IsNullOrWhiteSpace(input.HotelName) ? null : input.HotelName.Trim();
    trip.HotelCapacity = input.IncludesHotel ? input.HotelCapacity : null;
    trip.HasTwoFloors = preserveFloors ? trip.HasTwoFloors : input.HasTwoFloors;
    trip.Floor1Capacity = preserveFloors ? trip.Floor1Capacity : input.Floor1Capacity;
    trip.Floor2Capacity = preserveFloors ? trip.Floor2Capacity : input.Floor2Capacity;

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
    var bookingItems = await db.BookingItems.Where(i => bookingIds.Contains(i.BookingId)).ToListAsync();
    db.BookingItems.RemoveRange(bookingItems);
    var payments = await db.Payments.Where(p => bookingIds.Contains(p.BookingId)).ToListAsync();
    db.Payments.RemoveRange(payments);
    db.Bookings.RemoveRange(trip.Bookings);

    var capacityRequests = await db.CapacityRequests.Where(c => c.TripId == id).ToListAsync();
    db.CapacityRequests.RemoveRange(capacityRequests);

    // Las opciones del viaje deben borrarse antes que el viaje (FK Restrict).
    var tripOptions = await db.TripOptions.Where(o => o.TripId == id).ToListAsync();
    db.TripOptions.RemoveRange(tripOptions);

    // Los descuentos ligados al viaje se eliminan con el (las reservas guardan snapshot).
    var tripDiscounts = await db.Discounts.Where(d => d.TripId == id).ToListAsync();
    db.Discounts.RemoveRange(tripDiscounts);

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

app.MapGet("/api/users/me/wallet", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    if (userId == 0) return Results.Forbid();

    var (balance, held) = await WalletTotalsAsync(db, userId);

    var transactions = await db.WalletTransactions
        .Where(t => t.UserId == userId)
        .OrderByDescending(t => t.CreatedAt)
        .ToListAsync();

    return Results.Ok(new WalletSummary(balance, held, balance - held, transactions));
}).RequireAuthorization();

app.MapPost("/api/users/me/payout-requests", async (CreatePayoutRequest request, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    if (userId == 0) return Results.Forbid();

    // Lock in-process por usuario: evita sobre-congelar el saldo con dos
    // solicitudes concurrentes que leen el mismo disponible.
    var gate = WalletLockFor(userId);
    await gate.WaitAsync();
    try
    {
        if (request.Amount <= 0)
            return Results.BadRequest("El monto debe ser mayor a cero.");

        var (balance, held) = await WalletTotalsAsync(db, userId);
        var available = balance - held;

        if (request.Amount > available)
            return Results.BadRequest("El monto supera tu saldo disponible.");

        var user = await db.Users.FindAsync(userId);

        var payout = new PayoutRequest
        {
            UserId = userId,
            Amount = request.Amount,
            Status = PayoutStatus.Pending,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        db.PayoutRequests.Add(payout);
        await db.SaveChangesAsync();

        await SendToStaffAsync(db, $"{user?.Name ?? "Un cliente"} solicito un reembolso de {request.Amount:C} de su saldo.");

        var (newBalance, newHeld) = await WalletTotalsAsync(db, userId);
        return Results.Ok(new { PayoutRequest = payout, Balance = newBalance, Held = newHeld, Available = newBalance - newHeld });
    }
    finally
    {
        gate.Release();
    }
}).RequireAuthorization();

app.MapGet("/api/users/me/payout-requests", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    if (userId == 0) return Results.Forbid();

    var payouts = await db.PayoutRequests
        .Where(p => p.UserId == userId)
        .OrderByDescending(p => p.CreatedAt)
        .ToListAsync();

    return Results.Ok(payouts);
}).RequireAuthorization();

app.MapGet("/api/payout-requests", async (PayoutStatus? status, AppDbContext db) =>
{
    var query = db.PayoutRequests.AsQueryable();
    if (status.HasValue)
        query = query.Where(p => p.Status == status.Value);

    var payouts = await query
        .OrderByDescending(p => p.CreatedAt)
        .ToListAsync();

    return Results.Ok(payouts);
}).RequireAuthorization("StaffOnly");

app.MapPost("/api/payout-requests/{id}/resolve", async (int id, ResolvePayoutRequest request, ClaimsPrincipal principal, AppDbContext db) =>
{
    var payout = await db.PayoutRequests.FindAsync(id);
    if (payout is null) return Results.NotFound("Solicitud no encontrada.");

    // Lock in-process por usuario dueno del payout. El lock cubre la
    // reverificacion de estado y la escritura para no resolver (y debitar) dos
    // veces la misma solicitud.
    var gate = WalletLockFor(payout.UserId);
    await gate.WaitAsync();
    try
    {
        // Recargar dentro del lock: la copia cargada arriba puede estar
        // desactualizada si otra request la resolvio mientras esperabamos.
        await db.Entry(payout).ReloadAsync();

        if (payout.Status != PayoutStatus.Pending)
            return Results.BadRequest("La solicitud ya fue resuelta.");

        payout.ResolvedAt = DateTime.UtcNow;
        payout.ResolvedByUserId = GetUserId(principal);

        if (request.Approve)
        {
            payout.Status = PayoutStatus.Paid;
            db.WalletTransactions.Add(new WalletTransaction
            {
                UserId = payout.UserId,
                Amount = -payout.Amount,
                Type = WalletType.PayoutRequest,
                PayoutRequestId = payout.Id,
                Note = "Payout",
                CreatedAt = DateTime.UtcNow
            });
            db.Notifications.Add(new UserNotification
            {
                UserId = payout.UserId,
                Message = $"Tu reembolso de {payout.Amount:C} fue pagado.",
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            payout.Status = PayoutStatus.Rejected;
            db.Notifications.Add(new UserNotification
            {
                UserId = payout.UserId,
                Message = "Tu solicitud de reembolso fue rechazada; el saldo sigue disponible.",
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return Results.Ok(payout);
    }
    finally
    {
        gate.Release();
    }
}).RequireAuthorization("AdminOnly");

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

app.MapPut("/api/users/{id}", async (int id, UpdateCoordinatorRequest request, AppDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound("Usuario no encontrado.");
    if (user.Role != UserRole.Coordinador)
        return Results.BadRequest("Solo se pueden editar coordinadores.");

    var name = request.Name?.Trim();
    var email = request.Email?.Trim().ToLowerInvariant();

    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest("El nombre del coordinador es obligatorio.");
    if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        return Results.BadRequest("El correo no es válido.");
    if (await db.Users.AnyAsync(u => u.Email == email && u.Id != id))
        return Results.Conflict("Ya existe una cuenta con ese correo.");

    user.Name = name.Length <= 120 ? name : name[..120];
    user.Email = email;
    user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

    await db.SaveChangesAsync();
    return Results.Ok(user);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/users/{id}/reset-password", async (int id, AppDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound("Usuario no encontrado.");
    if (user.Role != UserRole.Coordinador)
        return Results.BadRequest("Solo se puede resetear la contraseña de un coordinador.");

    var password = GenerateTemporaryPassword();
    user.PasswordHash = HashPassword(password);
    await db.SaveChangesAsync();

    return Results.Ok(new ResetPasswordResponse { TemporaryPassword = password });
}).RequireAuthorization("AdminOnly");

app.MapDelete("/api/users/{id}", async (int id, AppDbContext db, ClaimsPrincipal principal) =>
{
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound("Usuario no encontrado.");
    if (user.Role != UserRole.Coordinador)
        return Results.BadRequest("Solo se pueden eliminar coordinadores.");
    if (user.Id == GetUserId(principal))
        return Results.BadRequest("No puedes eliminar tu propia cuenta.");
    if (await db.Bookings.AnyAsync(b => b.UserId == id))
        return Results.BadRequest("No se puede eliminar: el coordinador tiene reservas registradas.");

    // Dependencias con FK Restrict: se limpian antes de borrar el usuario.
    db.Notifications.RemoveRange(await db.Notifications.Where(n => n.UserId == id).ToListAsync());
    db.FavoriteTrips.RemoveRange(await db.FavoriteTrips.Where(f => f.UserId == id).ToListAsync());
    db.CapacityRequests.RemoveRange(await db.CapacityRequests.Where(c => c.UserId == id).ToListAsync());
    db.TripRatings.RemoveRange(await db.TripRatings.Where(r => r.UserId == id).ToListAsync());

    db.Users.Remove(user);
    await db.SaveChangesAsync();
    return Results.NoContent();
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
        .Include(b => b.Items)
            .ThenInclude(i => i.TripOption)
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
        .Include(b => b.Items)
            .ThenInclude(i => i.TripOption)
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

    var optionsMode = request.UseOptions == true || (request.Options != null && request.Options.Count > 0);
    Dictionary<int, TravelAgency.Shared.Models.TripOption>? optionsLookup = null;
    if (!optionsMode)
    {
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

        if (request.NumberOfSeats > 0 && requestedSeats.Count != request.NumberOfSeats)
            return Results.BadRequest($"Debes elegir exactamente {request.NumberOfSeats} asiento(s).");

        if (requestedSeats.Distinct().Count() != requestedSeats.Count)
            return Results.BadRequest("No puedes elegir el mismo asiento dos veces.");

        if (requestedSeats.Any(s => s < 1 || s > trip.Capacity))
            return Results.BadRequest("Uno de los asientos seleccionados no existe en este viaje.");

        var alreadyTaken = await GetTakenSeatNumbersAsync(db, trip);
        if (requestedSeats.Any(alreadyTaken.Contains))
            return Results.BadRequest("Alguno de los asientos seleccionados ya fue ocupado. Elige otros en el mapa.");
    }
    else
    {
        if (request.Options == null || request.Options.Count == 0)
            return Results.BadRequest("Debes indicar las opciones a reservar.");

        var optionIds = request.Options.Select(o => o.TripOptionId).Distinct().ToList();
        optionsLookup = await db.TripOptions
            .Where(o => o.TripId == trip.Id && optionIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id);

        var totalRequestedSeats = request.Options.Sum(o => Math.Max(0, o.Adults) + Math.Max(0, o.Children));
        if (totalRequestedSeats < 1)
            return Results.BadRequest("Cada línea de opción debe tener al menos 1 asiento.");
        if (totalRequestedSeats > trip.AvailableSeats)
            return Results.BadRequest("No hay suficientes lugares disponibles para este viaje.");

        foreach (var oi in request.Options)
        {
            if (!optionsLookup.TryGetValue(oi.TripOptionId, out var opt))
                return Results.BadRequest($"Opción {oi.TripOptionId} no válida para este viaje.");
            if (!opt.IsActive)
                return Results.BadRequest($"La opción \"{opt.Name}\" ya no está disponible.");
            if (Math.Max(0, oi.Adults) + Math.Max(0, oi.Children) < 1)
                return Results.BadRequest("Cada línea de opción debe tener al menos 1 asiento.");
        }
    }

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

    // Total base (con opciones si el modo multi-item esta activo).
    var itemsMode = (request.UseOptions == true || (request.Options != null && request.Options.Count > 0) || trip.HasOptions)
                    && request.Options != null && request.Options.Count > 0;
    decimal baseTotal;
    if (itemsMode)
    {
        baseTotal = 0m;
        foreach (var oi in request.Options!)
        {
            var opt = optionsLookup![oi.TripOptionId];
            baseTotal += Math.Max(0, oi.Adults) * opt.PriceAdult
                       + Math.Max(0, oi.Children) * (opt.PriceChild ?? opt.PriceAdult);
        }
        baseTotal = Math.Round(baseTotal, 2);
    }
    else
    {
        baseTotal = ComputeBookingTotal(trip, passengers);
    }

    // Descuento: validar y reservar el uso (incremento atomico) antes de crear la reserva.
    Discount? appliedDiscount = null;
    var discountAmount = 0m;
    if (!string.IsNullOrWhiteSpace(request.DiscountCode))
    {
        var code = request.DiscountCode.Trim();
        appliedDiscount = await db.Discounts.FirstOrDefaultAsync(d => d.Code.ToLower() == code.ToLower());
        if (appliedDiscount is null) return Results.BadRequest("El código de descuento no existe.");
        if (!appliedDiscount.IsActive) return Results.BadRequest("El código de descuento está inactivo.");
        if (!appliedDiscount.IsWithinWindow(DateTime.UtcNow)) return Results.BadRequest("El código de descuento no está vigente.");
        if (!appliedDiscount.AppliesToTrip(trip.Id)) return Results.BadRequest("El código de descuento no aplica a este viaje.");
        if (!appliedDiscount.HasUsesLeft) return Results.BadRequest("El código de descuento ya no tiene usos disponibles.");

        discountAmount = appliedDiscount.ComputeDiscount(baseTotal);

        var reserved = await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Discounts\" SET \"UsedCount\" = \"UsedCount\" + 1 WHERE \"Id\" = {0} AND (\"UsageLimit\" IS NULL OR \"UsedCount\" < \"UsageLimit\")",
            appliedDiscount.Id);
        if (reserved == 0)
            return Results.BadRequest("El código de descuento ya no tiene usos disponibles.");
    }

    var booking = new Booking
    {
        UserId = GetUserId(principal),
        TripId = request.TripId,
        NumberOfSeats = request.NumberOfSeats,
        SeatNumber = request.SeatNumber,
        BookingDate = DateTime.UtcNow,
        Status = BookingStatus.Pending,
        QrToken = GenerateQrToken(),
        DiscountCode = appliedDiscount?.Code,
        DiscountAmount = discountAmount,
        SpecialNeedsNote = string.IsNullOrWhiteSpace(request.SpecialNeedsNote)
            ? null
            : (request.SpecialNeedsNote.Trim().Length <= 500
                ? request.SpecialNeedsNote.Trim()
                : request.SpecialNeedsNote.Trim()[..500]),
        TotalAmount = Math.Max(0, Math.Round(baseTotal - discountAmount, 2))
    };

    // Lock in-process por usuario: cubre la lectura del saldo hasta el guardado
    // de la reserva y el pago con saldo, para no sobre-gastar el wallet. Los
    // return de validacion pasan por el finally.
    var walletGate = WalletLockFor(booking.UserId);
    await walletGate.WaitAsync();
    try
    {
        // Saldo de wallet aplicado al checkout. Se valida antes de crear la reserva.
        var walletAmount = request.WalletAmount is > 0 ? Math.Round(request.WalletAmount.Value, 2) : 0m;
        if (walletAmount > 0)
        {
            var (walletBalance, walletHeld) = await WalletTotalsAsync(db, booking.UserId);
            var walletMax = Math.Min(walletBalance - walletHeld, booking.TotalAmount);
            if (walletAmount > walletMax)
                return Results.BadRequest("El saldo aplicado supera tu saldo disponible o el total de la reserva.");
        }

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        if (walletAmount > 0)
        {
            var walletPayment = new Payment
            {
                BookingId = booking.Id,
                Amount = walletAmount,
                Method = PaymentMethod.Wallet,
                Status = PaymentStatus.Completed,
                PaymentDate = DateTime.UtcNow
            };
            db.Payments.Add(walletPayment);
            booking.Payments.Add(walletPayment);

            db.WalletTransactions.Add(new WalletTransaction
            {
                UserId = booking.UserId,
                Amount = -walletAmount,
                Type = WalletType.BookingPayment,
                BookingId = booking.Id,
                Note = "Pago de reserva con saldo",
                CreatedAt = DateTime.UtcNow
            });

            if (walletAmount >= booking.TotalAmount)
                booking.Status = BookingStatus.Confirmed;
        }

        // Guardar los items de opcion (multi-item) si aplica.
        var bookingItems = new List<TravelAgency.Shared.Models.BookingItem>();
        if (itemsMode)
        {
            foreach (var oi in request.Options!)
            {
                var opt = optionsLookup![oi.TripOptionId];
                bookingItems.Add(new TravelAgency.Shared.Models.BookingItem
                {
                    BookingId = booking.Id,
                    TripOptionId = opt.Id,
                    Adults = Math.Max(0, oi.Adults),
                    Children = Math.Max(0, oi.Children),
                    UnitPriceAdult = opt.PriceAdult,
                    UnitPriceChild = opt.PriceChild,
                    OptionName = opt.Name
                });
            }
            if (bookingItems.Count > 0)
            {
                booking.NumberOfSeats = bookingItems.Sum(i => i.Adults + i.Children);
                db.Bookings.Update(booking);
                db.BookingItems.AddRange(bookingItems);
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
    }
    finally
    {
        walletGate.Release();
    }

    var userName = await db.Users
        .Where(u => u.Id == booking.UserId)
        .Select(u => u.Name)
        .FirstOrDefaultAsync() ?? "Cliente";
    await SendToStaffAsync(db, $"Nueva reserva de {userName} para \"{trip.Title}\" ({booking.NumberOfSeats} asiento(s)).");

    if (!string.IsNullOrWhiteSpace(booking.SpecialNeedsNote))
        await SendToStaffAsync(db, $"Necesidades especiales de {userName} para \"{trip.Title}\": {booking.SpecialNeedsNote}");

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
        .Include(b => b.Items)
            .ThenInclude(i => i.TripOption)
        .FirstOrDefaultAsync(b => b.Id == id);

    if (booking is null) return Results.NotFound();

    if (GetUserId(principal) != booking.UserId && !principal.IsInRole("Admin"))
        return Results.Forbid();

    // Historico de reembolsos: permite mostrar el monto en cancelaciones antiguas
    // (pagadas como Refunded, sin credito en wallet) y en las nuevas.
    booking.RefundTotal = await db.BookingRefunds
        .Where(r => r.BookingId == id)
        .SumAsync(r => (decimal?)r.Amount) ?? 0m;

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

    // Lock in-process por usuario dueno de la reserva: cubre la lectura del
    // pendiente y la creacion del Payment para que dos pagos concurrentes no
    // excedan el saldo pendiente. La clave es el cliente, no el admin que cobra.
    var gate = WalletLockFor(booking.UserId);
    await gate.WaitAsync();
    try
    {
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
    }
    finally
    {
        gate.Release();
    }
}).RequireAuthorization("AdminOnly");

// Pago de una reserva existente con el saldo de la wallet. Lo puede hacer el
// titular (su propio saldo) o un Admin.
app.MapPost("/api/bookings/{id}/pay-with-wallet", async (int id, PayWithWalletRequest request, AppDbContext db, ClaimsPrincipal principal) =>
{
    var booking = await db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.User)
        .Include(b => b.Payments)
        .FirstOrDefaultAsync(b => b.Id == id);
    if (booking is null) return Results.NotFound("Reserva no encontrada.");

    var userId = GetUserId(principal);
    if (userId == 0) return Results.Forbid();
    if (booking.UserId != userId && !principal.IsInRole("Admin")) return Results.Forbid();

    if (booking.Status == BookingStatus.Cancelled)
        return Results.BadRequest("No se puede pagar una reserva cancelada.");

    if (request.Amount <= 0)
        return Results.BadRequest("Indica un monto valido.");

    // Lock in-process por usuario: cubre la lectura del saldo, la verificacion
    // del pendiente y la escritura, para no debitar dos veces el mismo saldo.
    var gate = WalletLockFor(booking.UserId);
    await gate.WaitAsync();
    try
    {
        var amount = Math.Round(request.Amount, 2);
        // Recalcular el pendiente contra la base dentro del lock: la copia con
        // Include cargada antes puede no ver un pago concurrente.
        var paidTotal = await db.Payments
            .Where(p => p.BookingId == booking.Id)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        var pending = Math.Max(0m, Math.Round(booking.TotalAmount - paidTotal, 2));
        if (pending <= 0)
            return Results.BadRequest("La reserva ya esta liquidada.");

        var (balance, held) = await WalletTotalsAsync(db, booking.UserId);
        var available = balance - held;

        if (amount > Math.Min(available, pending))
            return Results.BadRequest("El monto supera tu saldo disponible o el saldo pendiente de la reserva.");

        var walletPayment = new Payment
        {
            BookingId = booking.Id,
            Amount = amount,
            Method = PaymentMethod.Wallet,
            Status = PaymentStatus.Completed,
            PaymentDate = DateTime.UtcNow
        };
        db.Payments.Add(walletPayment);
        booking.Payments.Add(walletPayment);

        db.WalletTransactions.Add(new WalletTransaction
        {
            UserId = booking.UserId,
            Amount = -amount,
            Type = WalletType.BookingPayment,
            BookingId = booking.Id,
            Note = "Pago de reserva con saldo",
            CreatedAt = DateTime.UtcNow
        });

        if (amount >= pending)
            booking.Status = BookingStatus.Confirmed;

        db.Notifications.Add(new UserNotification
        {
            UserId = booking.UserId,
            Message = $"Pago de {amount:C} con saldo recibido. Saldo pendiente: {Math.Max(0m, pending - amount):C}.",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        return Results.Ok(booking);
    }
    finally
    {
        gate.Release();
    }
}).RequireAuthorization();

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

app.MapPost("/api/bookings/{id}/cancel", async (int id, CancelBookingRequest request, AppDbContext db, ClaimsPrincipal principal) =>
{
    var booking = await db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.User)
        .Include(b => b.Payments)
        .FirstOrDefaultAsync(b => b.Id == id);
    if (booking is null) return Results.NotFound("Reserva no encontrada.");

    var userId = GetUserId(principal);
    if (userId == 0 || booking.UserId != userId) return Results.Forbid();

    if (string.IsNullOrWhiteSpace(request.Reason))
        return Results.BadRequest("Indica el motivo de la cancelacion.");

    // Lock in-process por usuario dueno del wallet: cubre la validacion de
    // estado/politica, la creacion del BookingRefund y del WalletTransaction
    // (acreditacion) y el recompute de disponibilidad, para no acreditar dos
    // veces la misma cancelacion.
    var gate = WalletLockFor(booking.UserId);
    await gate.WaitAsync();
    try
    {
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

        // Fuente unica de lo abonado (igual que /cancel-ticket): pagos Completed.
        var paid = booking.PaidTotal();

        // Multa del 30% solo cuando se cancela fuera del limite de dias del viaje.
        var refund = BookingRefundPolicy.RefundFrom(paid, withinPolicy);
        var penalty = BookingRefundPolicy.PenaltyFrom(paid, withinPolicy);

        // El reembolso se acredita a la wallet del cliente; los pagos quedan como
        // Completed. La reserva, el BookingRefund y el WalletTransaction se guardan en
        // un solo SaveChangesAsync (transaccion implicita de EF), asi que un fallo no
        // deja el reembolso sin su credito. No se usa transaccion explicita porque el
        // AuditLogInterceptor abre su propia conexion en SavedChangesAsync y, con una
        // transaccion abierta, eso bloquearia en SQLite y quedaria fuera de la tx en Postgres.
        var refundEntity = new BookingRefund
        {
            BookingId = booking.Id,
            UserId = booking.UserId,
            Amount = refund,
            PenaltyAmount = penalty,
            Reason = request.Reason.Trim(),
            PassengerName = null,
            SeatNumber = null,
            BookingItemId = null,
            CreatedAt = DateTime.UtcNow
        };
        db.BookingRefunds.Add(refundEntity);

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancelledByUserId = userId;

        db.WalletTransactions.Add(new WalletTransaction
        {
            UserId = booking.UserId,
            Amount = refund,
            Type = WalletType.CancellationCredit,
            BookingId = booking.Id,
            Refund = refundEntity,
            Note = "Cancelacion total",
            CreatedAt = DateTime.UtcNow
        });

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
    }
    finally
    {
        gate.Release();
    }
}).RequireAuthorization();

// Cancelacion parcial: da de baja un boleto (acompanante) y acredita el reembolso
// a la wallet del cliente. El total, el descuento y los asientos se prorratean por boleto.
app.MapPost("/api/bookings/{id}/cancel-ticket", async (int id, CancelTicketRequest request, AppDbContext db, ClaimsPrincipal principal) =>
{
    var booking = await db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.User)
        .Include(b => b.Payments)
        .Include(b => b.Passengers)
        .Include(b => b.Items)
        .FirstOrDefaultAsync(b => b.Id == id);
    if (booking is null) return Results.NotFound("Reserva no encontrada.");

    var userId = GetUserId(principal);
    if (userId == 0) return Results.Forbid();
    if (booking.UserId != userId && !principal.IsInRole("Admin")) return Results.Forbid();

    // Lock in-process por usuario dueno del wallet: cubre la validacion de
    // estado/politica, la baja del boleto, la acreditacion y el recompute de
    // disponibilidad, para no acreditar dos veces el mismo boleto.
    var gate = WalletLockFor(booking.UserId);
    await gate.WaitAsync();
    try
    {
        if (booking.Status == BookingStatus.Cancelled)
            return Results.BadRequest("La reserva ya esta cancelada.");

        var trip = booking.Trip;
        if (trip is null) return Results.NotFound("El viaje de esta reserva ya no existe.");

        if (trip.DepartureCompleted)
            return Results.BadRequest("La salida ya se realizo, no es posible cancelar.");
        if (trip.Finalized)
            return Results.BadRequest("El viaje ya finalizo, no es posible cancelar.");

        var today = DateTime.UtcNow.Date;
        if (trip.StartDate.Date <= today)
            return Results.BadRequest("El viaje ya comenzo, no es posible cancelar.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.BadRequest("Indica el motivo de la cancelacion.");

        var passenger = booking.Passengers.FirstOrDefault(p => p.Id == request.PassengerId);
        if (passenger is null)
            return Results.BadRequest("El pasajero no pertenece a la reserva.");

        // Los TripPassenger son solo acompanantes: el titular es booking.SeatNumber y
        // viaja aparte (POST /api/bookings limita Passengers a NumberOfSeats - 1).
        // Guarda defensiva: el asiento del titular solo se cancela con la total.
        if (booking.SeatNumber.HasValue && passenger.SeatNumber.HasValue
            && passenger.SeatNumber.Value == booking.SeatNumber.Value)
            return Results.BadRequest("El asiento del titular solo se cancela con la cancelacion total de la reserva.");

        // Con opciones se elige la linea (BookingItem) a cancelar. Si hay mas de una,
        // BookingItemId es obligatorio; si hay una sola, se usa esa.
        BookingItem? item = null;
        if (trip.HasOptions)
        {
            if (booking.Items.Count > 1)
            {
                if (request.BookingItemId is not int itemId)
                    return Results.BadRequest("Selecciona la entrada del boleto a cancelar.");
                item = booking.Items.FirstOrDefault(i => i.Id == itemId);
                if (item is null)
                    return Results.BadRequest("La entrada no pertenece a la reserva.");
            }
            else
            {
                item = booking.Items.FirstOrDefault();
                if (request.BookingItemId is int singleId && item is not null && item.Id != singleId)
                    return Results.BadRequest("La entrada no pertenece a la reserva.");
            }
        }

        if (item is not null)
        {
            if (passenger.IsChild && item.Children <= 0)
                return Results.BadRequest("La entrada seleccionada no tiene boletos de nino.");
            if (!passenger.IsChild && item.Adults <= 0)
                return Results.BadRequest("La entrada seleccionada no tiene boletos de adulto.");
        }

        var paid = booking.PaidTotal();
        var grossBefore = booking.TotalAmount + booking.DiscountAmount;

        decimal grossUnitPrice;
        if (item is not null)
            grossUnitPrice = passenger.IsChild
                ? (item.UnitPriceChild ?? item.UnitPriceAdult)
                : item.UnitPriceAdult;
        else
            // Sin opciones el precio por asiento debe ser BRUTO (lista): TotalAmount
            // ya trae el descuento aplicado, asi que se reparte el subtotal con descuento.
            // Pasar un neto a NetUnitPrice descontaria dos veces.
            grossUnitPrice = booking.NumberOfSeats > 0
                ? grossBefore / booking.NumberOfSeats
                : grossBefore;

        var netUnit = BookingRefundPolicy.NetUnitPrice(booking, grossUnitPrice);
        var paidShare = BookingRefundPolicy.PaidShare(booking, paid, netUnit);
        var withinPolicy = BookingRefundPolicy.IsWithinPolicy(trip.StartDate, today, trip.CancellationDaysLimit);
        var refund = BookingRefundPolicy.RefundFrom(paidShare, withinPolicy);
        var penalty = BookingRefundPolicy.PenaltyFrom(paidShare, withinPolicy);

        // Baja de la linea elegida; si queda vacia se elimina.
        if (item is not null)
        {
            if (passenger.IsChild) item.Children--;
            else item.Adults--;
            if (item.Seats <= 0) db.BookingItems.Remove(item);
        }

        db.TripPassengers.Remove(passenger);
        if (booking.NumberOfSeats > 0) booking.NumberOfSeats--;

        if (grossBefore > 0)
        {
            var discountShare = Math.Round(booking.DiscountAmount * grossUnitPrice / grossBefore, 2);
            booking.DiscountAmount = Math.Max(0m, booking.DiscountAmount - discountShare);
        }
        booking.TotalAmount = Math.Max(0m, booking.TotalAmount - netUnit);

        if (booking.NumberOfSeats == 0)
        {
            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = DateTime.UtcNow;
            booking.CancelledByUserId = userId;
        }

        var refundEntity = new BookingRefund
        {
            BookingId = booking.Id,
            UserId = booking.UserId,
            Amount = refund,
            PenaltyAmount = penalty,
            Reason = request.Reason.Trim(),
            PassengerName = passenger.Name,
            SeatNumber = passenger.SeatNumber,
            BookingItemId = item?.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.BookingRefunds.Add(refundEntity);

        db.WalletTransactions.Add(new WalletTransaction
        {
            UserId = booking.UserId,
            Amount = refund,
            Type = WalletType.CancellationCredit,
            BookingId = booking.Id,
            Refund = refundEntity,
            Note = "Cancelacion de boleto",
            CreatedAt = DateTime.UtcNow
        });

        // Baja del boleto + BookingRefund + WalletTransaction en un solo SaveChangesAsync
        // (transaccion implicita de EF), atomico. Sin transaccion explicita: el
        // AuditLogInterceptor abre su propia conexion en SavedChangesAsync y una tx
        // abierta la bloquearia en SQLite y quedaria fuera de la tx en Postgres.
        await db.SaveChangesAsync();

        await RecomputeAvailabilityAsync(db, trip);
        await db.SaveChangesAsync();

        var walletBalance = await db.WalletTransactions
            .Where(t => t.UserId == booking.UserId)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        var seatLabel = passenger.SeatNumber.HasValue ? $"asiento {passenger.SeatNumber}" : "boleto";
        db.Notifications.Add(new UserNotification
        {
            UserId = booking.UserId,
            Message = $"Se cancelo el {seatLabel} y se acreditaron {refund:C} a tu saldo.",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        if (booking.User is not null)
            await SendToStaffAsync(db,
                $"El cliente {booking.User.Name} cancelo el {seatLabel} de su reserva para \"{trip.Title}\". Reembolso {refund:C}.");

        return Results.Ok(new CancelTicketResult(booking, refund, penalty, withinPolicy, walletBalance));
    }
    finally
    {
        gate.Release();
    }
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

app.MapPut("/api/trips/{id}/active", async (int id, UpdateTripActiveRequest request, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var wasActive = trip.IsActive;
    trip.IsActive = request.IsActive;
    await db.SaveChangesAsync();

    // Aviso a los clientes con reservas vigentes solo cuando el estado cambia.
    if (wasActive != request.IsActive)
    {
        var userIds = await db.Bookings
            .Where(b => b.TripId == id && b.Status != BookingStatus.Cancelled)
            .Select(b => b.UserId)
            .Distinct()
            .ToListAsync();

        if (userIds.Count > 0)
        {
            var message = request.IsActive
                ? $"El viaje \"{trip.Title}\" ya está disponible de nuevo. ¡Tu reserva sigue en pie!"
                : $"El viaje \"{trip.Title}\" fue pausado temporalmente. Tu reserva sigue vigente; te avisaremos cuando se reactive.";

            foreach (var userId in userIds)
            {
                db.Notifications.Add(new UserNotification
                {
                    UserId = userId,
                    Message = message,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }
    }

    return Results.Ok(trip);
}).RequireAuthorization("AdminOnly");

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


app.MapGet("/api/trips/{tripId}/options", async (int tripId, AppDbContext db) =>
    await db.TripOptions.Where(o => o.TripId == tripId).OrderBy(o => o.Order).ToListAsync());

app.MapPost("/api/trips/{tripId}/options", async (int tripId, TripOptionDto dto, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(tripId);
    if (trip is null) return Results.NotFound();

    if (string.IsNullOrWhiteSpace(dto.Name))
        return Results.BadRequest("El nombre de la opción es obligatorio.");
    if (dto.Name.Length > 80)
        return Results.BadRequest("El nombre de la opción no puede superar los 80 caracteres.");
    if (dto.PriceAdult < 0)
        return Results.BadRequest("El precio no puede ser negativo.");
    if (dto.PriceChild < 0)
        return Results.BadRequest("El precio de niño no puede ser negativo.");

    var opt = new TravelAgency.Shared.Models.TripOption
    {
        TripId = tripId,
        Name = dto.Name.Trim(),
        Description = dto.Description,
        PriceAdult = dto.PriceAdult,
        PriceChild = dto.PriceChild,
        Capacity = trip.BookableCapacity,
        AvailableSeats = trip.AvailableSeats,
        Benefits = dto.Benefits,
        IsActive = dto.IsActive,
        Order = dto.Order,
        CapacityMode = TravelAgency.Shared.Models.TripOptionCapacityMode.Shared,
        CreatedAt = DateTime.UtcNow
    };
    db.TripOptions.Add(opt);
    trip.HasOptions = true;
    await db.SaveChangesAsync();
    await RecomputeAvailabilityAsync(db, trip);
    await db.SaveChangesAsync();
    return Results.Created($"/api/trips/{tripId}/options/{opt.Id}", opt);
}).RequireAuthorization("StaffOnly");

app.MapPut("/api/trips/{tripId}/options/{optionId}", async (int tripId, int optionId, TripOptionDto dto, AppDbContext db) =>
{
    var opt = await db.TripOptions.FirstOrDefaultAsync(o => o.Id == optionId && o.TripId == tripId);
    if (opt is null) return Results.NotFound();

    if (string.IsNullOrWhiteSpace(dto.Name))
        return Results.BadRequest("El nombre de la opción es obligatorio.");
    if (dto.Name.Length > 80)
        return Results.BadRequest("El nombre de la opción no puede superar los 80 caracteres.");
    if (dto.PriceAdult < 0)
        return Results.BadRequest("El precio no puede ser negativo.");
    if (dto.PriceChild < 0)
        return Results.BadRequest("El precio de niño no puede ser negativo.");

    opt.Name = dto.Name.Trim();
    opt.Description = dto.Description;
    opt.PriceAdult = dto.PriceAdult;
    opt.PriceChild = dto.PriceChild;
    opt.Benefits = dto.Benefits;
    opt.IsActive = dto.IsActive;
    opt.Order = dto.Order;

    var trip = await db.Trips.FindAsync(tripId);
    if (trip is not null) await RecomputeAvailabilityAsync(db, trip);

    await db.SaveChangesAsync();
    return Results.Ok(opt);
}).RequireAuthorization("StaffOnly");

app.MapDelete("/api/trips/{tripId}/options/{optionId}", async (int tripId, int optionId, AppDbContext db) =>
{
    var opt = await db.TripOptions.FirstOrDefaultAsync(o => o.Id == optionId && o.TripId == tripId);
    if (opt is null) return Results.NotFound();
    if (opt.IsBase)
        return Results.BadRequest("La entrada general no se puede eliminar. Puedes desactivarla si no la usas.");
    if (await db.BookingItems.AnyAsync(i => i.TripOptionId == optionId))
        return Results.BadRequest("Esta opción tiene reservas registradas y no se puede eliminar. Puedes desactivarla.");
    db.TripOptions.Remove(opt);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization("StaffOnly");

// ===== Descuentos =====
app.MapGet("/api/discounts", async (AppDbContext db) =>
    await db.Discounts
        .Include(d => d.Trip)
        .OrderByDescending(d => d.CreatedAt)
        .ToListAsync()).RequireAuthorization("AdminOnly");

app.MapPost("/api/discounts", async (DiscountDto dto, AppDbContext db) =>
{
    var error = ValidateDiscountDto(dto);
    if (error is not null) return Results.BadRequest(error);

    var code = dto.Code.Trim().ToUpperInvariant();
    if (await db.Discounts.AnyAsync(d => d.Code.ToLower() == code.ToLower()))
        return Results.BadRequest("Ya existe un descuento con ese código.");

    var discount = new Discount
    {
        Code = code,
        Type = dto.Type,
        Value = dto.Value,
        StartDate = dto.StartDate?.Date,
        EndDate = dto.EndDate?.Date,
        UsageLimit = dto.UsageLimit,
        UsedCount = 0,
        IsActive = dto.IsActive,
        TripId = dto.TripId,
        CreatedAt = DateTime.UtcNow
    };
    db.Discounts.Add(discount);
    await db.SaveChangesAsync();
    return Results.Created($"/api/discounts/{discount.Id}", discount);
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/discounts/{id}", async (int id, DiscountDto dto, AppDbContext db) =>
{
    var discount = await db.Discounts.FirstOrDefaultAsync(d => d.Id == id);
    if (discount is null) return Results.NotFound();

    var error = ValidateDiscountDto(dto);
    if (error is not null) return Results.BadRequest(error);

    var code = dto.Code.Trim().ToUpperInvariant();
    if (await db.Discounts.AnyAsync(d => d.Id != id && d.Code.ToLower() == code.ToLower()))
        return Results.BadRequest("Ya existe un descuento con ese código.");

    discount.Code = code;
    discount.Type = dto.Type;
    discount.Value = dto.Value;
    discount.StartDate = dto.StartDate?.Date;
    discount.EndDate = dto.EndDate?.Date;
    discount.UsageLimit = dto.UsageLimit;
    discount.IsActive = dto.IsActive;
    discount.TripId = dto.TripId;
    await db.SaveChangesAsync();
    return Results.Ok(discount);
}).RequireAuthorization("AdminOnly");

app.MapDelete("/api/discounts/{id}", async (int id, AppDbContext db) =>
{
    var discount = await db.Discounts.FirstOrDefaultAsync(d => d.Id == id);
    if (discount is null) return Results.NotFound();
    db.Discounts.Remove(discount);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/discounts/validate", async (ValidateDiscountRequest request, AppDbContext db) =>
{
    var baseAmount = Math.Max(0, request.BaseAmount);

    if (string.IsNullOrWhiteSpace(request.Code))
        return Results.Ok(new DiscountValidationResult(false, "Escribe un código de descuento.", 0, baseAmount, null));

    var code = request.Code.Trim();
    var discount = await db.Discounts.FirstOrDefaultAsync(d => d.Code.ToLower() == code.ToLower());
    if (discount is null)
        return Results.Ok(new DiscountValidationResult(false, "El código no existe.", 0, baseAmount, null));
    if (!discount.IsActive)
        return Results.Ok(new DiscountValidationResult(false, "El código está inactivo.", 0, baseAmount, null));
    if (!discount.IsWithinWindow(DateTime.UtcNow))
        return Results.Ok(new DiscountValidationResult(false, "El código no está vigente.", 0, baseAmount, null));
    if (!discount.AppliesToTrip(request.TripId))
        return Results.Ok(new DiscountValidationResult(false, "El código no aplica a este viaje.", 0, baseAmount, null));
    if (!discount.HasUsesLeft)
        return Results.Ok(new DiscountValidationResult(false, "El código ya no tiene usos disponibles.", 0, baseAmount, null));

    var amount = discount.ComputeDiscount(baseAmount);
    return Results.Ok(new DiscountValidationResult(
        true, "Descuento aplicado.", amount, Math.Max(0, Math.Round(baseAmount - amount, 2)), discount.Code, discount.Type, discount.Value));
}).RequireAuthorization();

app.Run();

static async Task RecomputeAvailabilityAsync(AppDbContext db, Trip trip)
{
    var sold = await db.Bookings
        .Where(b => b.TripId == trip.Id && b.Status != BookingStatus.Cancelled)
        .SumAsync(b => (int?)b.NumberOfSeats) ?? 0;
    trip.AvailableSeats = trip.BookableCapacity - sold;

    // Cuando el viaje tiene opciones, todas comparten el mismo cupo del viaje.
    if (trip.HasOptions)
    {
        var options = await db.TripOptions.Where(o => o.TripId == trip.Id).ToListAsync();
        foreach (var opt in options)
        {
            opt.AvailableSeats = trip.AvailableSeats;
            opt.Capacity = trip.BookableCapacity;
        }
    }
}

static string? ValidateHotelFields(Trip trip)
{
    if (!trip.IncludesHotel) return null;
    if (!trip.HotelCapacity.HasValue || trip.HotelCapacity.Value < 1)
        return "Indica cuántos lugares hay disponibles según el hotel (mínimo 1).";
    if (trip.HotelName?.Length > 150)
        return "El nombre del hotel no puede superar los 150 caracteres.";
    return null;
}

// Dos pisos solo aplican al camion/autobus. Si aplica, Capacity = piso1 + piso2;
// si no, se normaliza a planta unica. Devuelve mensaje de error o null.
static string? NormalizeAndValidateFloors(Trip trip)
{
    if (trip.HasTwoFloors && trip.TransportType == TransportType.Camion)
    {
        if (!trip.Floor1Capacity.HasValue || trip.Floor1Capacity.Value < 1)
            return "El cupo del piso 1 debe ser al menos 1.";
        if (!trip.Floor2Capacity.HasValue || trip.Floor2Capacity.Value < 1)
            return "El cupo del piso 2 debe ser al menos 1.";

        trip.Capacity = trip.Floor1Capacity.Value + trip.Floor2Capacity.Value;
    }
    else
    {
        trip.HasTwoFloors = false;
        trip.Floor1Capacity = null;
        trip.Floor2Capacity = null;
    }

    return null;
}

static string? ValidateDiscountDto(DiscountDto dto)
{
    if (string.IsNullOrWhiteSpace(dto.Code)) return "El código es obligatorio.";
    if (dto.Code.Trim().Length > 60) return "El código no puede superar los 60 caracteres.";
    if (dto.Value <= 0) return "El valor del descuento debe ser mayor a 0.";
    if (dto.Type == DiscountType.Percentage && dto.Value > 100)
        return "El porcentaje no puede ser mayor a 100.";
    if (dto.UsageLimit is < 1)
        return "El límite de uso debe ser al menos 1 (o vacío para ilimitado).";
    if (dto.StartDate.HasValue && dto.EndDate.HasValue && dto.EndDate.Value.Date < dto.StartDate.Value.Date)
        return "La fecha de fin no puede ser anterior a la de inicio.";
    return null;
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

static async Task<(decimal Balance, decimal Held)> WalletTotalsAsync(AppDbContext db, int userId)
{
    var balance = await db.WalletTransactions
        .Where(t => t.UserId == userId)
        .SumAsync(t => (decimal?)t.Amount) ?? 0m;

    var held = await db.PayoutRequests
        .Where(p => p.UserId == userId && p.Status == PayoutStatus.Pending)
        .SumAsync(p => (decimal?)p.Amount) ?? 0m;

    return (balance, held);
}

SemaphoreSlim WalletLockFor(int userId) =>
    walletLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

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

static List<TripSeatRow> BuildSeatRows(int floor1Capacity, int floor2Capacity, Dictionary<int, TripSeat> assigned)
{
    const int leftSeats = 2;
    const int rightSeats = 2;
    const int aisleSlot = leftSeats;
    const int slotsPerRow = leftSeats + 1 + rightSeats;
    const int seatsPerRow = leftSeats + rightSeats;
    var rows = new List<TripSeatRow>();

    // Agrega las filas de un piso. Los asientos se numeran de forma global y continua:
    // el piso 2 empieza en startSeat (F1 + 1) y su RowNumber reinicia en 1.
    void AddFloor(int floor, int startSeat, int floorCapacity)
    {
        if (floorCapacity <= 0) return;
        var totalRows = (int)Math.Ceiling(floorCapacity / (double)seatsPerRow);
        for (var rowIndex = 0; rowIndex < totalRows; rowIndex++)
        {
            var row = new TripSeatRow { RowNumber = rowIndex + 1, Floor = floor };
            var seatIndex = startSeat - 1 + rowIndex * seatsPerRow;

            for (var slot = 0; slot < slotsPerRow; slot++)
            {
                if (slot == aisleSlot)
                {
                    row.Seats.Add(new TripSeat { IsAisle = true });
                    continue;
                }

                seatIndex++;
                if (seatIndex > startSeat - 1 + floorCapacity) break;

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
    }

    if (floor1Capacity <= 0 && floor2Capacity <= 0) return rows;

    AddFloor(1, 1, floor1Capacity);
    if (floor2Capacity > 0) AddFloor(2, floor1Capacity + 1, floor2Capacity);

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

static async Task EnsureTripHotelColumnsAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Trips", "IncludesHotel", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? @"ALTER TABLE ""Trips"" ADD COLUMN ""IncludesHotel"" INTEGER NOT NULL DEFAULT 0;"
            : @"ALTER TABLE ""Trips"" ADD COLUMN ""IncludesHotel"" boolean NOT NULL DEFAULT false;");
    }

    if (!await ColumnExistsAsync(db, "Trips", "HotelName", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? @"ALTER TABLE ""Trips"" ADD COLUMN ""HotelName"" TEXT NULL;"
            : @"ALTER TABLE ""Trips"" ADD COLUMN ""HotelName"" character varying(150) NULL;");
    }

    if (!await ColumnExistsAsync(db, "Trips", "HotelCapacity", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? @"ALTER TABLE ""Trips"" ADD COLUMN ""HotelCapacity"" INTEGER NULL;"
            : @"ALTER TABLE ""Trips"" ADD COLUMN ""HotelCapacity"" integer NULL;");
    }
}


static async Task EnsureTripFloorsColumnsAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Trips", "HasTwoFloors", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? @"ALTER TABLE ""Trips"" ADD COLUMN ""HasTwoFloors"" INTEGER NOT NULL DEFAULT 0;"
            : @"ALTER TABLE ""Trips"" ADD COLUMN ""HasTwoFloors"" boolean NOT NULL DEFAULT false;");
    }

    if (!await ColumnExistsAsync(db, "Trips", "Floor1Capacity", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? @"ALTER TABLE ""Trips"" ADD COLUMN ""Floor1Capacity"" INTEGER NULL;"
            : @"ALTER TABLE ""Trips"" ADD COLUMN ""Floor1Capacity"" integer NULL;");
    }

    if (!await ColumnExistsAsync(db, "Trips", "Floor2Capacity", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? @"ALTER TABLE ""Trips"" ADD COLUMN ""Floor2Capacity"" INTEGER NULL;"
            : @"ALTER TABLE ""Trips"" ADD COLUMN ""Floor2Capacity"" integer NULL;");
    }
}


static async Task EnsureTripOptionIsBaseColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "TripOptions", "IsBase", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? @"ALTER TABLE ""TripOptions"" ADD COLUMN ""IsBase"" INTEGER NOT NULL DEFAULT 0;"
            : @"ALTER TABLE ""TripOptions"" ADD COLUMN ""IsBase"" boolean NOT NULL DEFAULT false;");
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
    // Seguridad Supabase: RLS activo (el API usa el rol service/owner, que lo omite).
    await TryExecAsync(db, @"ALTER TABLE ""TripOptions"" ENABLE ROW LEVEL SECURITY;");
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
    // Seguridad Supabase: RLS activo (el API usa el rol service/owner, que lo omite).
    await TryExecAsync(db, @"ALTER TABLE ""BookingItems"" ENABLE ROW LEVEL SECURITY;");
}


static async Task EnsureDiscountsTableAsync(AppDbContext db, string provider)
{
    if (provider == "sqlite")
    {
        await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""Discounts"" (
              ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Discounts"" PRIMARY KEY AUTOINCREMENT,
              ""Code"" TEXT NOT NULL,
              ""Type"" INTEGER NOT NULL DEFAULT 0,
              ""Value"" TEXT NOT NULL DEFAULT '0',
              ""StartDate"" TEXT NULL,
              ""EndDate"" TEXT NULL,
              ""UsageLimit"" INTEGER NULL,
              ""UsedCount"" INTEGER NOT NULL DEFAULT 0,
              ""IsActive"" INTEGER NOT NULL DEFAULT 1,
              ""TripId"" INTEGER NULL,
              ""CreatedAt"" TEXT NOT NULL,
              CONSTRAINT ""FK_Discounts_Trips_TripId"" FOREIGN KEY (""TripId"") REFERENCES ""Trips"" (""Id"") ON DELETE CASCADE
          );");
        await TryExecAsync(db, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Discounts_Code"" ON ""Discounts"" (""Code"");");
        await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_Discounts_TripId"" ON ""Discounts"" (""TripId"");");
        return;
    }

    await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""Discounts"" (
          ""Id"" integer GENERATED BY DEFAULT AS IDENTITY NOT NULL,
          ""Code"" character varying(60) NOT NULL,
          ""Type"" integer NOT NULL DEFAULT 0,
          ""Value"" numeric(18,2) NOT NULL DEFAULT 0,
          ""StartDate"" timestamp with time zone NULL,
          ""EndDate"" timestamp with time zone NULL,
          ""UsageLimit"" integer NULL,
          ""UsedCount"" integer NOT NULL DEFAULT 0,
          ""IsActive"" boolean NOT NULL DEFAULT true,
          ""TripId"" integer NULL,
          ""CreatedAt"" timestamp with time zone NOT NULL,
          CONSTRAINT ""PK_Discounts"" PRIMARY KEY (""Id""),
          CONSTRAINT ""FK_Discounts_Trips_TripId"" FOREIGN KEY (""TripId"") REFERENCES ""Trips"" (""Id"") ON DELETE CASCADE
      );");
    await TryExecAsync(db, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Discounts_Code"" ON ""Discounts"" (""Code"");");
    await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_Discounts_TripId"" ON ""Discounts"" (""TripId"");");
    // Seguridad Supabase: RLS activo (el API usa el rol service/owner, que lo omite).
    await TryExecAsync(db, @"ALTER TABLE ""Discounts"" ENABLE ROW LEVEL SECURITY;");
}


static async Task EnsureBookingDiscountColumnsAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Bookings", "DiscountCode", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Bookings\" ADD COLUMN \"DiscountCode\" TEXT NULL;"
            : "ALTER TABLE \"Bookings\" ADD COLUMN \"DiscountCode\" character varying(60) NULL;");
    }

    if (!await ColumnExistsAsync(db, "Bookings", "DiscountAmount", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Bookings\" ADD COLUMN \"DiscountAmount\" TEXT NOT NULL DEFAULT '0';"
            : "ALTER TABLE \"Bookings\" ADD COLUMN \"DiscountAmount\" numeric(18,2) NOT NULL DEFAULT 0;");
    }
}


static async Task EnsureBookingSpecialNeedsColumnAsync(AppDbContext db, string provider)
{
    if (!await ColumnExistsAsync(db, "Bookings", "SpecialNeedsNote", provider))
    {
        await TryExecAsync(db, provider == "sqlite"
            ? "ALTER TABLE \"Bookings\" ADD COLUMN \"SpecialNeedsNote\" TEXT NULL;"
            : "ALTER TABLE \"Bookings\" ADD COLUMN \"SpecialNeedsNote\" character varying(500) NULL;");
    }
}


static async Task EnsureDefaultTripOptionsBackfillAsync(AppDbContext db, string provider)
{
    try
    {
        var trips = await db.Trips
            .Select(t => new { t.Id, t.Price, t.ChildPrice, t.Capacity, t.AvailableSeats, t.HasOptions, t.IncludesHotel, t.HotelCapacity })
            .ToListAsync();
        if (trips.Count == 0) return;

        var options = await db.TripOptions.ToListAsync();
        var byTrip = options.GroupBy(o => o.TripId).ToDictionary(g => g.Key, g => g.ToList());

        var now = DateTime.UtcNow;
        var changed = false;

        foreach (var t in trips)
        {
            byTrip.TryGetValue(t.Id, out var opts);
            opts ??= new List<TravelAgency.Shared.Models.TripOption>();

            if (opts.Any(o => o.IsBase)) continue;

            // Adoptar una opción existente como base (por nombre "General" o por
            // tener el mismo precio base del viaje, para no duplicar si fue renombrada).
            var candidate = opts.FirstOrDefault(o => string.Equals(o.Name, "General", StringComparison.OrdinalIgnoreCase))
                            ?? opts.FirstOrDefault(o => o.PriceAdult == t.Price && o.PriceChild == t.ChildPrice);
            if (candidate is not null)
            {
                candidate.IsBase = true;
                changed = true;
                continue;
            }

            var bookable = t.IncludesHotel && t.HotelCapacity.HasValue
                ? Math.Min(t.Capacity, Math.Max(0, t.HotelCapacity.Value))
                : t.Capacity;

            db.TripOptions.Add(new TravelAgency.Shared.Models.TripOption
            {
                TripId = t.Id,
                Name = "General",
                Description = null,
                PriceAdult = t.Price,
                PriceChild = t.ChildPrice,
                Capacity = bookable,
                AvailableSeats = Math.Max(0, t.AvailableSeats),
                Benefits = null,
                IsActive = true,
                Order = opts.Count == 0 ? 1 : Math.Max(0, opts.Min(o => o.Order) - 1),
                CapacityMode = TravelAgency.Shared.Models.TripOptionCapacityMode.Shared,
                IsBase = true,
                CreatedAt = now
            });
            changed = true;

            if (!t.HasOptions)
            {
                var trip = await db.Trips.FirstAsync(x => x.Id == t.Id);
                trip.HasOptions = true;
            }
        }

        if (changed) await db.SaveChangesAsync();
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

static async Task EnsureBookingRefundsTableAsync(AppDbContext db, string provider)
{
    if (provider == "sqlite")
    {
        await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""BookingRefunds"" (
              ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_BookingRefunds"" PRIMARY KEY AUTOINCREMENT,
              ""BookingId"" INTEGER NOT NULL,
              ""UserId"" INTEGER NOT NULL,
              ""Amount"" TEXT NOT NULL,
              ""PenaltyAmount"" TEXT NOT NULL,
              ""Reason"" TEXT NOT NULL,
              ""PassengerName"" TEXT NULL,
              ""SeatNumber"" INTEGER NULL,
              ""BookingItemId"" INTEGER NULL,
              ""CreatedAt"" TEXT NOT NULL,
              CONSTRAINT ""FK_BookingRefunds_Bookings_BookingId"" FOREIGN KEY (""BookingId"") REFERENCES ""Bookings"" (""Id"") ON DELETE RESTRICT,
              CONSTRAINT ""FK_BookingRefunds_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE RESTRICT
          );");
        await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_BookingRefunds_BookingId"" ON ""BookingRefunds"" (""BookingId"");");
        await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_BookingRefunds_UserId"" ON ""BookingRefunds"" (""UserId"");");
        return;
    }

    await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""BookingRefunds"" (
          ""Id"" integer GENERATED BY DEFAULT AS IDENTITY NOT NULL,
          ""BookingId"" integer NOT NULL,
          ""UserId"" integer NOT NULL,
          ""Amount"" numeric(18,2) NOT NULL,
          ""PenaltyAmount"" numeric(18,2) NOT NULL,
          ""Reason"" character varying(500) NOT NULL,
          ""PassengerName"" character varying(120) NULL,
          ""SeatNumber"" integer NULL,
          ""BookingItemId"" integer NULL,
          ""CreatedAt"" timestamp with time zone NOT NULL,
          CONSTRAINT ""PK_BookingRefunds"" PRIMARY KEY (""Id""),
          CONSTRAINT ""FK_BookingRefunds_Bookings_BookingId"" FOREIGN KEY (""BookingId"") REFERENCES ""Bookings"" (""Id"") ON DELETE RESTRICT,
          CONSTRAINT ""FK_BookingRefunds_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE RESTRICT
      );");
    await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_BookingRefunds_BookingId"" ON ""BookingRefunds"" (""BookingId"");");
    await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_BookingRefunds_UserId"" ON ""BookingRefunds"" (""UserId"");");
    // Seguridad Supabase: RLS activo (el API usa el rol service/owner, que lo omite).
    await TryExecAsync(db, @"ALTER TABLE ""BookingRefunds"" ENABLE ROW LEVEL SECURITY;");
}

static async Task EnsureWalletTransactionsTableAsync(AppDbContext db, string provider)
{
    if (provider == "sqlite")
    {
        await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""WalletTransactions"" (
              ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_WalletTransactions"" PRIMARY KEY AUTOINCREMENT,
              ""UserId"" INTEGER NOT NULL,
              ""Amount"" TEXT NOT NULL,
              ""Type"" INTEGER NOT NULL DEFAULT 0,
              ""BookingId"" INTEGER NULL,
              ""RefundId"" INTEGER NULL,
              ""Note"" TEXT NULL,
              ""CreatedAt"" TEXT NOT NULL,
              CONSTRAINT ""FK_WalletTransactions_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE RESTRICT
          );");
        await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_WalletTransactions_UserId"" ON ""WalletTransactions"" (""UserId"");");
        return;
    }

    await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""WalletTransactions"" (
          ""Id"" integer GENERATED BY DEFAULT AS IDENTITY NOT NULL,
          ""UserId"" integer NOT NULL,
          ""Amount"" numeric(18,2) NOT NULL,
          ""Type"" integer NOT NULL DEFAULT 0,
          ""BookingId"" integer NULL,
          ""RefundId"" integer NULL,
          ""Note"" character varying(300) NULL,
          ""CreatedAt"" timestamp with time zone NOT NULL,
          CONSTRAINT ""PK_WalletTransactions"" PRIMARY KEY (""Id""),
          CONSTRAINT ""FK_WalletTransactions_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE RESTRICT
      );");
    await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_WalletTransactions_UserId"" ON ""WalletTransactions"" (""UserId"");");
    // Seguridad Supabase: RLS activo (el API usa el rol service/owner, que lo omite).
    await TryExecAsync(db, @"ALTER TABLE ""WalletTransactions"" ENABLE ROW LEVEL SECURITY;");
}

static async Task EnsureWalletTransactionPayoutColumnAsync(AppDbContext db, string provider)
{
    // Columna sin FK: el vinculado se resuelve por PayoutRequestId, que puede
    // ser null en los movimientos que no provienen de un retiro.
    if (!await ColumnExistsAsync(db, "WalletTransactions", "PayoutRequestId", provider))
    {
        var columnType = provider == "sqlite" ? "INTEGER NULL" : "integer NULL";
        await TryExecAsync(db, $@"ALTER TABLE ""WalletTransactions"" ADD COLUMN ""PayoutRequestId"" {columnType};");
    }

    await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_WalletTransactions_PayoutRequestId"" ON ""WalletTransactions"" (""PayoutRequestId"");");
}

static async Task EnsurePayoutRequestsTableAsync(AppDbContext db, string provider)
{
    if (provider == "sqlite")
    {
        await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""PayoutRequests"" (
              ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PayoutRequests"" PRIMARY KEY AUTOINCREMENT,
              ""UserId"" INTEGER NOT NULL,
              ""Amount"" TEXT NOT NULL,
              ""Status"" INTEGER NOT NULL DEFAULT 0,
              ""Note"" TEXT NULL,
              ""CreatedAt"" TEXT NOT NULL,
              ""ResolvedAt"" TEXT NULL,
              ""ResolvedByUserId"" INTEGER NULL,
              CONSTRAINT ""FK_PayoutRequests_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE RESTRICT
          );");
        await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_PayoutRequests_UserId"" ON ""PayoutRequests"" (""UserId"");");
        await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_PayoutRequests_Status"" ON ""PayoutRequests"" (""Status"");");
        return;
    }

    await TryExecAsync(db, @"CREATE TABLE IF NOT EXISTS ""PayoutRequests"" (
          ""Id"" integer GENERATED BY DEFAULT AS IDENTITY NOT NULL,
          ""UserId"" integer NOT NULL,
          ""Amount"" numeric(18,2) NOT NULL,
          ""Status"" integer NOT NULL DEFAULT 0,
          ""Note"" character varying(300) NULL,
          ""CreatedAt"" timestamp with time zone NOT NULL,
          ""ResolvedAt"" timestamp with time zone NULL,
          ""ResolvedByUserId"" integer NULL,
          CONSTRAINT ""PK_PayoutRequests"" PRIMARY KEY (""Id""),
          CONSTRAINT ""FK_PayoutRequests_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE RESTRICT
      );");
    await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_PayoutRequests_UserId"" ON ""PayoutRequests"" (""UserId"");");
    await TryExecAsync(db, @"CREATE INDEX IF NOT EXISTS ""IX_PayoutRequests_Status"" ON ""PayoutRequests"" (""Status"");");
    // Seguridad Supabase: RLS activo (el API usa el rol service/owner, que lo omite).
    await TryExecAsync(db, @"ALTER TABLE ""PayoutRequests"" ENABLE ROW LEVEL SECURITY;");
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
record CreateBookingRequest(int TripId, int NumberOfSeats, List<PassengerInput>? Passengers, int? SeatNumber = null, List<BookingOptionInput>? Options = null, bool? UseOptions = null, string? DiscountCode = null, string? SpecialNeedsNote = null, decimal? WalletAmount = null);
record CreatePassengersRequest(List<PassengerInput>? Passengers);
record CancelBookingRequest(string? Reason);
record CancelTicketRequest(int PassengerId, int? BookingItemId, string? Reason);
record PayWithWalletRequest(decimal Amount);
record UpdateDepartureRequest(bool? CheckInOpen, bool? DepartureCompleted);
record UpdateFinalizeRequest(bool Finalized);
record UpdateTripActiveRequest(bool IsActive);
record UpdateTripRatingRequest(int Rating, string? Comment);
record DeleteTripRequest(string? Message);
record UpdateTripRouteRequest(double? OriginLatitude, double? OriginLongitude, double? DestinationLatitude, double? DestinationLongitude);
record TripOptionDto(string Name, string? Description, decimal PriceAdult, decimal? PriceChild, string? Benefits, bool IsActive, int Order);
record DiscountDto(string Code, DiscountType Type, decimal Value, DateTime? StartDate, DateTime? EndDate, int? UsageLimit, bool IsActive, int? TripId);
record ValidateDiscountRequest(string? Code, int TripId, decimal BaseAmount);
record DiscountValidationResult(bool Valid, string Message, decimal DiscountAmount, decimal FinalAmount, string? Code, DiscountType? Type = null, decimal? Value = null);
record UpsertPoiRequest(string? Name, string? Description, double Latitude, double Longitude);
record CreatePayoutRequest(decimal Amount, string? Note);
record ResolvePayoutRequest(bool Approve);