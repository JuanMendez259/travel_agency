namespace TravelAgency.Shared.Models;

/// Reembolso generado por una cancelacion total o parcial.
/// En una cancelacion parcial `BookingItemId`, `PassengerName` y `SeatNumber`
/// identifican el boleto reembolsado; en la total pueden quedar en null.
public class BookingRefund
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public decimal PenaltyAmount { get; set; }
    public string Reason { get; set; } = "";
    public string? PassengerName { get; set; }
    public int? SeatNumber { get; set; }
    public int? BookingItemId { get; set; }
    public DateTime CreatedAt { get; set; }
}
