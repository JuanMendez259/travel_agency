using Microsoft.EntityFrameworkCore;
using TravelAgency.Shared.Models;

namespace TravelAgency.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<UserNotification> Notifications => Set<UserNotification>();
    public DbSet<CapacityRequest> CapacityRequests => Set<CapacityRequest>();
    public DbSet<TripPointOfInterest> TripPointsOfInterest => Set<TripPointOfInterest>();
    public DbSet<TripPassenger> TripPassengers => Set<TripPassenger>();
    public DbSet<TripRating> TripRatings => Set<TripRating>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Name).HasMaxLength(120).IsRequired();
            entity.Property(u => u.Email).HasMaxLength(254).IsRequired();
            entity.Property(u => u.Phone).HasMaxLength(30);
        });

        modelBuilder.Entity<Trip>(entity =>
        {
            entity.Property(t => t.Title).HasMaxLength(200).IsRequired();
            entity.Property(t => t.Destination).HasMaxLength(150).IsRequired();
            entity.Property(t => t.Price).HasPrecision(18, 2);
            entity.Property(t => t.ChildPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.Property(b => b.TotalAmount).HasPrecision(18, 2);
            entity.HasIndex(b => b.QrToken).IsUnique();
            entity.HasOne(b => b.User)
                  .WithMany(u => u.Bookings)
                  .HasForeignKey(b => b.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(b => b.Trip)
                  .WithMany(t => t.Bookings)
                  .HasForeignKey(b => b.TripId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.Property(p => p.Amount).HasPrecision(18, 2);
            entity.HasOne(p => p.Booking)
                  .WithMany(b => b.Payments)
                  .HasForeignKey(p => p.BookingId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TripPassenger>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(120);
            entity.HasIndex(p => p.BookingId);
            entity.HasOne(p => p.Booking)
                  .WithMany(b => b.Passengers)
                  .HasForeignKey(p => p.BookingId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TripRating>(entity =>
        {
            entity.Property(r => r.UserName).HasMaxLength(120);
            entity.Property(r => r.Destination).HasMaxLength(150);
            entity.Property(r => r.Comment).HasMaxLength(1000);
            entity.Property(r => r.Price).HasPrecision(18, 2);
            entity.HasIndex(r => r.TripId);
            entity.HasIndex(r => new { r.UserId, r.TripId }).IsUnique();
            entity.HasOne(r => r.Trip)
                  .WithMany()
                  .HasForeignKey(r => r.TripId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.User)
                  .WithMany()
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserNotification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.Property(n => n.Message).HasMaxLength(1000).IsRequired();
            entity.HasIndex(n => n.UserId);
            entity.HasOne(n => n.User)
                  .WithMany(u => u.Notifications)
                  .HasForeignKey(n => n.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CapacityRequest>(entity =>
        {
            entity.Property(c => c.Message).HasMaxLength(1000);
            entity.HasIndex(c => c.TripId);
            entity.HasOne(c => c.Trip)
                  .WithMany(t => t.CapacityRequests)
                  .HasForeignKey(c => c.TripId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(c => c.User)
                  .WithMany()
                  .HasForeignKey(c => c.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TripPointOfInterest>(entity =>
        {
            entity.ToTable("TripPointsOfInterest");
            entity.Property(p => p.Name).HasMaxLength(200);
            entity.Property(p => p.Description).HasMaxLength(1000);
            entity.HasIndex(p => p.TripId);
            entity.HasOne(p => p.Trip)
                  .WithMany(t => t.PointsOfInterest)
                  .HasForeignKey(p => p.TripId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.Property(a => a.UserName).HasMaxLength(120);
            entity.Property(a => a.EntityType).HasMaxLength(30).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(20).IsRequired();
            entity.Property(a => a.Summary).HasMaxLength(1000);
            entity.HasIndex(a => new { a.EntityType, a.EntityId });
            entity.HasIndex(a => a.CreatedAt);
        });

        modelBuilder.Entity<User>().HasData(
            new User { Id = 1, Name = "Administrador", Email = "admin@travelagency.com", PasswordHash = "CHANGE_ME", Role = UserRole.Admin, CreatedAt = DateTime.UtcNow }
        );

        modelBuilder.Entity<Trip>().HasData(
            new Trip
            {
                Id = 1,
                Title = "Escapada a Quintana Roo",
                Destination = "Cancún, México",
                Description = "Una semana frente al Caribe con hotel todo incluido y traslados.",
                StartDate = new DateTime(2026, 10, 10),
                EndDate = new DateTime(2026, 10, 17),
                Price = 12500m,
                AvailableSeats = 20,
                Capacity = 20,
                TransportType = TransportType.Camion,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Trip
            {
                Id = 2,
                Title = "Descubre Machu Picchu",
                Destination = "Cusco, Perú",
                Description = "Circuito completo por el Valle Sagrado con guía en español.",
                StartDate = new DateTime(2026, 11, 5),
                EndDate = new DateTime(2026, 11, 12),
                Price = 9800m,
                AvailableSeats = 15,
                Capacity = 15,
                TransportType = TransportType.Camioneta,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }
        );
    }
}