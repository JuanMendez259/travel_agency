using TravelAgency.Shared.Models;

namespace TravelAgency.App.Services;

public class SessionService
{
    public AuthResponse? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;
    public bool IsAdmin => CurrentUser?.Role == UserRole.Admin;
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