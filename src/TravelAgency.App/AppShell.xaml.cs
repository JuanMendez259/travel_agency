namespace TravelAgency.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("trip", typeof(TravelAgency.App.Modules.Client.Views.ClientTripDetailPage));
        Routing.RegisterRoute("booking", typeof(TravelAgency.App.Modules.Admin.Views.AdminBookingDetailPage));
    }
}