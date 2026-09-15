namespace TravelAgency.Shared.Models;

public class User
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public string? Phone { get; set; }
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}

public enum UserRole
{
    Client = 0,
    Admin = 1
}