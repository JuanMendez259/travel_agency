namespace TravelAgency.Shared.Models;

public enum DiscountType
{
    Percentage = 0,
    Amount = 1
}

/// <summary>
/// Codigo de descuento administrable. Puede ser global (TripId null, aplica a
/// cualquier viaje) o ligado a un viaje especifico. Se aplica sobre el total
/// final de la reserva.
/// </summary>
public class Discount
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public DiscountType Type { get; set; }

    /// <summary>Porcentaje (0-100) o monto fijo en MXN, segun <see cref="Type"/>.</summary>
    public decimal Value { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    /// <summary>Limite total de usos entre todos los clientes. Null = ilimitado.</summary>
    public int? UsageLimit { get; set; }

    public int UsedCount { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Null = global; con valor = solo aplica a ese viaje.</summary>
    public int? TripId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Trip? Trip { get; set; }

    public bool IsGlobal => TripId is null;

    public bool HasUsesLeft => UsageLimit is null || UsedCount < UsageLimit;

    public bool IsWithinWindow(DateTime now) =>
        (!StartDate.HasValue || now.Date >= StartDate.Value.Date) &&
        (!EndDate.HasValue || now.Date <= EndDate.Value.Date);

    public bool AppliesToTrip(int tripId) => TripId is null || TripId == tripId;

    /// <summary>Monto a descontar sobre un total base (nunca negativo ni mayor al base).</summary>
    public decimal ComputeDiscount(decimal baseAmount)
    {
        if (baseAmount <= 0) return 0;

        var raw = Type == DiscountType.Percentage
            ? baseAmount * Value / 100m
            : Value;

        var discount = Math.Round(raw, 2);
        if (discount < 0) discount = 0;
        if (discount > baseAmount) discount = baseAmount;
        return discount;
    }
}
