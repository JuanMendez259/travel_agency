namespace TravelAgency.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("trip", typeof(TravelAgency.App.Modules.Client.Views.ClientTripDetailPage));
        Routing.RegisterRoute("favorites", typeof(TravelAgency.App.Modules.Client.Views.ClientFavoritesPage));
        Routing.RegisterRoute("mybooking", typeof(TravelAgency.App.Modules.Client.Views.ClientMyBookingDetailPage));
        Routing.RegisterRoute("passengersqr", typeof(TravelAgency.App.Modules.Client.Views.ClientPassengersQrPage));
        Routing.RegisterRoute("addpassengers", typeof(TravelAgency.App.Modules.Client.Views.AddPassengersPage));
        Routing.RegisterRoute("bookseats", typeof(TravelAgency.App.Modules.Client.Views.ClientBookingSeatsPage));
        Routing.RegisterRoute("ratetrip", typeof(TravelAgency.App.Modules.Client.Views.ClientRateTripPage));
        Routing.RegisterRoute("booking", typeof(TravelAgency.App.Modules.Admin.Views.AdminBookingDetailPage));
        Routing.RegisterRoute("admintrip", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripDetailPage));
        Routing.RegisterRoute("tripbookings", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripBookingsPage));
        Routing.RegisterRoute("tripmap", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripMapPage));
        Routing.RegisterRoute("tripseats", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripSeatsPage));
        Routing.RegisterRoute("auditlog", typeof(TravelAgency.App.Modules.Admin.Views.AdminAuditPage));
        Routing.RegisterRoute("checkin", typeof(TravelAgency.App.Modules.Admin.Views.AdminCheckinPage));
        Routing.RegisterRoute("scancheckin", typeof(TravelAgency.App.Modules.Admin.Views.AdminScanQrPage));
        Routing.RegisterRoute("bookingqr", typeof(TravelAgency.App.Modules.Admin.Views.AdminBookingQrPage));
    }
}