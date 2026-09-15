using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new { Service = "TravelAgency.Api", Status = "OK" }));

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
});

app.MapGet("/api/users", async (AppDbContext db) =>
    await db.Users.ToListAsync());

app.MapGet("/api/bookings", async (AppDbContext db) =>
    await db.Bookings.Include(b => b.Trip).Include(b => b.User).ToListAsync());

app.MapPost("/api/bookings", async (Booking booking, AppDbContext db) =>
{
    var trip = await db.Trips.FindAsync(booking.TripId);
    if (trip is null) return Results.NotFound("Viaje no encontrado.");

    var user = await db.Users.FindAsync(booking.UserId);
    if (user is null) return Results.NotFound("Usuario no encontrado.");

    if (booking.NumberOfSeats > trip.AvailableSeats)
        return Results.BadRequest("No hay suficientes asientos disponibles.");

    booking.BookingDate = DateTime.UtcNow;
    booking.Status = BookingStatus.Pending;
    booking.TotalAmount = trip.Price * booking.NumberOfSeats;

    trip.AvailableSeats -= booking.NumberOfSeats;
    db.Bookings.Add(booking);
    await db.SaveChangesAsync();
    return Results.Created($"/api/bookings/{booking.Id}", booking);
});

app.MapGet("/api/bookings/{id}", async (int id, AppDbContext db) =>
    await db.Bookings
        .Include(b => b.Trip)
        .Include(b => b.User)
        .Include(b => b.Payments)
        .FirstOrDefaultAsync(b => b.Id == id) is Booking booking ? Results.Ok(booking) : Results.NotFound());

app.MapGet("/api/bookings/{id}/payments", async (int id, AppDbContext db) =>
    await db.Payments.Where(p => p.BookingId == id).ToListAsync());

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
});

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
});

app.Run();

record UpdateBookingStatusRequest(BookingStatus Status);