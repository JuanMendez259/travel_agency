using Microsoft.Extensions.DependencyInjection;
using TravelAgency.App.Modules.Auth.Views;
using TravelAgency.App.Services;

namespace TravelAgency.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        Services = services;
#if WINDOWS
        Microsoft.UI.Xaml.Application.Current.UnhandledException += (_, e) =>
        {
            CrashLogger.Write("WinUI", e.Exception);
            if (e.Exception is InvalidOperationException ioe &&
                ioe.Message.Contains("Pending Navigations", StringComparison.OrdinalIgnoreCase))
                e.Handled = true;
        };
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(Services.GetRequiredService<LoginPage>());
    }

    public static void GoToMainShell()
    {
        var shell = Services.GetRequiredService<AppShell>();
        var session = Services.GetRequiredService<SessionService>();

        var tabs = shell.Items.SelectMany(i => i.Items).SelectMany(s => s.Items).ToList();

        if (session.IsAdmin)
        {
            foreach (var item in tabs)
                item.IsVisible = item.Route is "admintrips" or "bookings" or "admin";
        }
        else
        {
            foreach (var item in tabs)
                item.IsVisible = item.Route is "home" or "mytrips";
        }

        Application.Current!.Windows[0].Page = shell;
    }

    public static void GoToLogin()
    {
        var session = Services.GetRequiredService<SessionService>();
        session.Clear();
        Services.GetRequiredService<ApiService>().SetAuthToken(null);

        Application.Current!.Windows[0].Page = Services.GetRequiredService<LoginPage>();
    }
}