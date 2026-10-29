namespace TravelAgency.Shared.Models;

public static class RoomBookingRules
{
    public const int MinOccupancy = 1;
    public const int MaxOccupancy = 8;

    public static string? Validate(
        int? roomOccupancy,
        bool tripIncludesHotel,
        int adults,
        int children,
        out int? rooms)
    {
        rooms = null;

        if (roomOccupancy is null) return null;

        if (!tripIncludesHotel)
            return "La opción tiene habitación pero el viaje no incluye hotel.";
        if (roomOccupancy is < MinOccupancy or > MaxOccupancy)
            return $"La ocupación de la habitación debe estar entre {MinOccupancy} y {MaxOccupancy} personas.";
        if (adults < 0 || children < 0)
            return "Las cantidades de adultos y niños no pueden ser negativas.";

        var seats = adults + children;
        if (seats < roomOccupancy.Value)
            return $"La habitación requiere al menos {roomOccupancy} persona(s).";
        if (seats % roomOccupancy.Value != 0)
            return $"La cantidad de personas debe ser múltiplo de {roomOccupancy}.";

        var roomCount = seats / roomOccupancy.Value;
        if (children > seats - roomCount)
            return "Cada habitación debe tener al menos un adulto.";

        rooms = roomCount;
        return null;
    }

    public static int? Rooms(int? roomOccupancy, int adults, int children)
    {
        if (roomOccupancy is not > 0) return null;
        var seats = adults + children;
        return seats / roomOccupancy.Value;
    }
}
