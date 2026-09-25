using Microsoft.Extensions.Logging;
using TravelAgency.App.Modules.Admin.Views;
using TravelAgency.App.Modules.Auth.Views;
using TravelAgency.App.Modules.Client.Views;
using TravelAgency.App.Services;
using ZXing.Net.Maui;

namespace TravelAgency.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		CrashLogger.Attach();

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseBarcodeReader()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<ApiService>();
		builder.Services.AddSingleton<SessionService>();
		builder.Services.AddTransient<AppShell>();
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<ClientHomePage>();
        builder.Services.AddTransient<ClientFavoritesPage>();
builder.Services.AddTransient<ClientTripDetailPage>();
        builder.Services.AddTransient<ClientMyTripsPage>();
        builder.Services.AddTransient<ClientProfilePage>();
        builder.Services.AddTransient<ClientPassengersQrPage>();
        builder.Services.AddTransient<ClientRateTripPage>();
        builder.Services.AddTransient<AddPassengersPage>();
        builder.Services.AddTransient<ClientNotificationsPage>();
		builder.Services.AddTransient<ClientMyBookingDetailPage>();
		builder.Services.AddTransient<AdminDashboardPage>();
builder.Services.AddTransient<AdminTripsPage>();
        builder.Services.AddTransient<AdminTripDetailPage>();
        builder.Services.AddTransient<AdminTripBookingsPage>();
        builder.Services.AddTransient<AdminTripMapPage>();
        builder.Services.AddTransient<AdminTripSeatsPage>();
        builder.Services.AddTransient<AdminStatsPage>();
        builder.Services.AddTransient<AdminAuditPage>();
        builder.Services.AddTransient<AdminCheckinPage>();
        builder.Services.AddTransient<AdminScanQrPage>();
        builder.Services.AddTransient<AdminBookingQrPage>();
		builder.Services.AddTransient<AdminBookingsPage>();
		builder.Services.AddTransient<AdminBookingDetailPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}