using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.ViewModels;

public class BookingCardItem
{
    public Booking Booking { get; }
    public ImageSource? Thumb { get; }

    public BookingCardItem(Booking booking, ImageSource? thumb)
    {
        Booking = booking;
        Thumb = thumb;
    }
}