using TravelAgency.Shared.Models;

namespace TravelAgency.App.Services;

public static class TripContactPolicy
{
    public static bool CanOfferContact(Trip trip, Booking booking, DateTime today)
    {
        var date = today.Date;
        return !trip.Finalized
            && booking.Status != BookingStatus.Cancelled
            && !booking.CheckedIn
            && date >= trip.StartDate.Date
            && date <= trip.EndDate.Date;
    }
}
