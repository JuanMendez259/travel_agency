using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.ViewModels;

public class TripBookingItem
{
    public Booking Booking { get; }

    public string Client => Booking.User?.Name ?? Booking.User?.Email ?? $"Usuario #{Booking.UserId}";
    public int Seats => Booking.NumberOfSeats;
    public decimal Total => Booking.TotalAmount;
    public decimal Paid => Booking.Payments?.Sum(p => p.Amount) ?? 0;
    public decimal Remaining => Math.Max(0, Total - Paid);
    public string BalanceText => Remaining <= 0m ? "Liquidado" : $"Pagado {Paid:C} · Saldo {Remaining:C}";
    public string Status => Booking.Status.ToString();

    public TripBookingItem(Booking booking)
    {
        Booking = booking;
    }
}