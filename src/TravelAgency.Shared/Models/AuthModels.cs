namespace TravelAgency.Shared.Models;

public class LoginRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public class RegisterRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public class AuthResponse
{
    public string? Token { get; set; }
    public int UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public UserRole Role { get; set; }
}

public class UpdateUserRoleRequest
{
    public UserRole Role { get; set; }
}