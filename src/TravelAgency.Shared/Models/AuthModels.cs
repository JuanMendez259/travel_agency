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
    public string? Phone { get; set; }
    public string? EmergencyContact { get; set; }
}

public class AuthResponse
{
    public string? Token { get; set; }
    public int UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public UserRole Role { get; set; }
}

public class SocialLoginRequest
{
    public string? AccessToken { get; set; }
}

public class SocialAuthResponse
{
    public string? Token { get; set; }
    public int UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public UserRole Role { get; set; }
    public bool PerfilIncompleto { get; set; }
    public string? Proveedor { get; set; }
}

public class UpdateProfileRequest
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? EmergencyContact { get; set; }
}

public class UpdateUserRoleRequest
{
    public UserRole Role { get; set; }
}

public class CreateCoordinatorRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Password { get; set; }
}

public class CreatedCoordinator
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public string TemporaryPassword { get; set; } = string.Empty;
}

public class UpdateCoordinatorRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class ResetPasswordResponse
{
    public string TemporaryPassword { get; set; } = string.Empty;
}