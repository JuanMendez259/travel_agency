namespace TravelAgency.Shared.Models;

/// <summary>
/// Gasto o inversion que el administrador registra para un viaje especifico.
/// Sirve para llevar el control del dinero invertido en cada viaje.
/// </summary>
public class TripExpense
{
    public int Id { get; set; }

    /// <summary>Viaje al que pertenece el gasto.</summary>
    public int TripId { get; set; }

    /// <summary>Concepto o descripcion del gasto.</summary>
    public string Concept { get; set; } = string.Empty;

    /// <summary>Monto del gasto en MXN.</summary>
    public decimal Amount { get; set; }

    public DateTime CreatedAt { get; set; }
}
