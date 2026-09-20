using TravelAgency.Shared.Models;

namespace TravelAgency.App.Services;

public class SessionService
{
    public AuthResponse? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;
    public bool IsAdmin => CurrentUser?.Role == UserRole.Admin;
    public bool IsCoordinator => CurrentUser?.Role == UserRole.Coordinador;
    public bool IsStaff => CurrentUser?.Role is UserRole.Admin or UserRole.Coordinador;
    public UserRole Role => CurrentUser?.Role ?? UserRole.Client;
    public int UserId => CurrentUser?.UserId ?? 0;
    public string? UserName => CurrentUser?.Name;

    public void Start(AuthResponse auth)
    {
        CurrentUser = auth;
    }

    public void Clear()
    {
        CurrentUser = null;
    }
}