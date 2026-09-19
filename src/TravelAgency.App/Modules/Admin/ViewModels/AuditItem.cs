using System.Text.Json;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.ViewModels;

public class AuditItem
{
    private static readonly Dictionary<string, string> FieldNames = new()
    {
        ["Title"] = "Título",
        ["Destination"] = "Destino",
        ["Description"] = "Descripción",
        ["StartDate"] = "Fecha de inicio",
        ["EndDate"] = "Fecha de fin",
        ["Price"] = "Precio",
        ["Capacity"] = "Capacidad",
        ["AvailableSeats"] = "Asientos disponibles",
        ["TransportType"] = "Transporte",
        ["IsActive"] = "Activo",
        ["NumberOfSeats"] = "Asientos",
        ["Status"] = "Estado",
        ["TotalAmount"] = "Total",
        ["Amount"] = "Monto",
        ["Method"] = "Método",
        ["TransactionReference"] = "Referencia"
    };

    public string Summary { get; }
    public string WhoWhen { get; }
    public string DiffText { get; }
    public bool HasDiff => !string.IsNullOrEmpty(DiffText);

    public AuditItem(AuditLog log)
    {
        Summary = log.Summary;

        var who = string.IsNullOrWhiteSpace(log.UserName) ? "Sistema" : log.UserName;
        var when = log.CreatedAt.Date == DateTime.Today
            ? log.CreatedAt.ToString("HH:mm")
            : log.CreatedAt.ToString("dd/MM/yyyy HH:mm");
        WhoWhen = $"{who} · {when}";

        DiffText = BuildDiffText(log.Details);
    }

    private static string BuildDiffText(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
            return "";

        try
        {
            var diff = JsonSerializer.Deserialize<Dictionary<string, string[]>>(detailsJson);
            if (diff is null || diff.Count == 0)
                return "";

            var lines = diff
                .OrderBy(kv => kv.Key)
                .Select(kv =>
                {
                    var name = FieldNames.TryGetValue(kv.Key, out var label) ? label : kv.Key;
                    var values = kv.Value;
                    var from = values.Length > 0 ? values[0] : "";
                    var to = values.Length > 1 ? values[1] : "";
                    return $"  · {name}: {Format(from)} → {Format(to)}";
                });

            return string.Join("\n", lines);
        }
        catch
        {
            return "";
        }
    }

    private static string Format(string value)
    {
        if (string.IsNullOrEmpty(value)) return "—";
        if (value == "True") return "Sí";
        if (value == "False") return "No";
        return value;
    }
}