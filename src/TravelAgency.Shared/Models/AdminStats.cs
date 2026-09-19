namespace TravelAgency.Shared.Models;

public class AdminStats
{
    public int TripsCount { get; set; }
    public int BookingsCount { get; set; }
    public int SeatsSold { get; set; }
    public decimal RevenueTotal { get; set; }
    public double OccupancyPercent { get; set; }

    public List<MonthlyRevenue> MonthlyRevenue { get; set; } = new();
    public List<TripOccupancy> OccupancyByTrip { get; set; } = new();
    public List<TopDestination> TopDestinations { get; set; } = new();
}

public class MonthlyRevenue
{
    public string Month { get; set; } = "";
    public decimal Amount { get; set; }
}

public class TripOccupancy
{
    public int TripId { get; set; }
    public string Title { get; set; } = "";
    public int Capacity { get; set; }
    public int Sold { get; set; }
    public double Percent { get; set; }

    public double Progress => Percent / 100.0;
    public string Detail => $"{Sold} de {Capacity} asientos · {Percent:0.#}%";
}

public class TopDestination
{
    public string Destination { get; set; } = "";
    public int Seats { get; set; }

    public string SeatsLabel => $"{Seats} asiento(s)";
}