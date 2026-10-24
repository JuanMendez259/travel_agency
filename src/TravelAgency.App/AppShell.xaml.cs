namespace TravelAgency.App;

public partial class AppShell : Shell
{
    private readonly HashSet<Page> _seenPages = new();
    private readonly HashSet<Page> _attachedPages = new();
    private bool _recovering;

    public AppShell()
    {
        InitializeComponent();
        Navigated += (_, _) => CheckCurrentPageHandler();
        Routing.RegisterRoute("profilesetup", typeof(TravelAgency.App.Modules.Auth.Views.ProfileSetupPage));
        Routing.RegisterRoute("trip", typeof(TravelAgency.App.Modules.Client.Views.ClientTripDetailPage));
        Routing.RegisterRoute("favorites", typeof(TravelAgency.App.Modules.Client.Views.ClientFavoritesPage));
        Routing.RegisterRoute("mybooking", typeof(TravelAgency.App.Modules.Client.Views.ClientMyBookingDetailPage));
        Routing.RegisterRoute("imageviewer", typeof(TravelAgency.App.Modules.Client.Views.ClientImageViewerPage));
        Routing.RegisterRoute("passengersqr", typeof(TravelAgency.App.Modules.Client.Views.ClientPassengersQrPage));
        Routing.RegisterRoute("addpassengers", typeof(TravelAgency.App.Modules.Client.Views.AddPassengersPage));
        Routing.RegisterRoute("bookseats", typeof(TravelAgency.App.Modules.Client.Views.ClientBookingSeatsPage));
        Routing.RegisterRoute("ratetrip", typeof(TravelAgency.App.Modules.Client.Views.ClientRateTripPage));
        Routing.RegisterRoute("booking", typeof(TravelAgency.App.Modules.Admin.Views.AdminBookingDetailPage));
        Routing.RegisterRoute("admintrip", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripDetailPage));
        Routing.RegisterRoute("tripoptions", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripOptionsPage));
        Routing.RegisterRoute("tripbookings", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripBookingsPage));
        Routing.RegisterRoute("tripmap", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripMapPage));
        Routing.RegisterRoute("clienttripmap", typeof(TravelAgency.App.Modules.Client.Views.ClientTripMapPage));
        Routing.RegisterRoute("tripseats", typeof(TravelAgency.App.Modules.Admin.Views.AdminTripSeatsPage));
        Routing.RegisterRoute("auditlog", typeof(TravelAgency.App.Modules.Admin.Views.AdminAuditPage));
        Routing.RegisterRoute("checkin", typeof(TravelAgency.App.Modules.Admin.Views.AdminCheckinPage));
        Routing.RegisterRoute("scancheckin", typeof(TravelAgency.App.Modules.Admin.Views.AdminScanQrPage));
        Routing.RegisterRoute("bookingqr", typeof(TravelAgency.App.Modules.Admin.Views.AdminBookingQrPage));
    }

    private void CheckCurrentPageHandler()
    {
        if (_recovering)
            return;

        var page = CurrentPage;
        if (page is null)
            return;

        if (_seenPages.Add(page))
        {
            page.HandlerChanged += (_, _) =>
            {
                if (page.Handler is not null)
                    _attachedPages.Add(page);
            };
        }

        if (page.Handler is not null)
        {
            _attachedPages.Add(page);
            return;
        }

        if (!_attachedPages.Contains(page))
            return;

        var route = CurrentState?.Location?.OriginalString;
        if (string.IsNullOrWhiteSpace(route))
            return;

        _recovering = true;
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                await Task.Delay(50);
                if (ReferenceEquals(CurrentPage, page) && page.Handler is null)
                    App.RecoverShell(route);
            }
            finally
            {
                _recovering = false;
            }
        });
    }
}
