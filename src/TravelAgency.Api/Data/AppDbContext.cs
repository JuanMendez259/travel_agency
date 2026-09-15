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
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.Property(b => b.TotalAmount).HasPrecision(18, 2);
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
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }
        );
    }
}