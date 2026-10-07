namespace TravelAgency.App.Services;

public record BookingOptionSelection(int TripOptionId, int Adults, int Children, int Seats);

public class BookingSelectionStore
{
    private static readonly Lazy<BookingSelectionStore> _instance = new(() => new BookingSelectionStore());

    public static BookingSelectionStore Instance => _instance.Value;

    private BookingSelectionStore()
    {
    }

    public int TripId { get; set; }
    public List<BookingOptionSelection> Options { get; set; } = new();
    public int TotalSeats { get; set; }
    public string? SpecialNeedsNote { get; set; }

    public void Clear()
    {
        TripId = 0;
        Options.Clear();
        TotalSeats = 0;
        SpecialNeedsNote = null;
    }
}
