using TravelAgency.Shared.Models;

namespace TravelAgency.App.Converters;

public static class SeatLabels
{
    // Formatea un asiento para mostrar: "12" en una planta, "25 · Piso 2" en dos pisos.
    public static string Format(Trip? trip, int? seat)
    {
        if (seat is null) return "—";

        if (trip?.HasTwoFloors == true)
        {
            var floor = trip.Floor1Capacity is int f1 && seat.Value > f1 ? 2 : 1;
            return $"{seat.Value} · Piso {floor}";
        }

        return seat.Value.ToString();
    }
}
