namespace TravelAgency.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("trip", typeof(TravelAgency.App.Modules.Client.Views.ClientTripDetailPage));
        Routing.RegisterRoute("mybooking", typeof(TravelAgency.App.Modules.Client.Views.ClientMyBookingDetailPage));
        Routing.RegisterRoute("booking", typeof(TravelAgency.App.Modules.Admin.Views.AdminBookingDetailPage));
        Routing.RegisterRoute("admintrip", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripDetailPage));
        Routing.RegisterRoute("tripbookings", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripBookingsPage));
        Routing.RegisterRoute("tripmap", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripMapPage));
        Routing.RegisterRoute("auditlog", typeof(TravelAgency.App.Modules.Admin.Views.AdminAuditPage));
        Routing.RegisterRoute("checkin", typeof(TravelAgency.App.Modules.Admin.Views.AdminCheckinPage));
    }
}