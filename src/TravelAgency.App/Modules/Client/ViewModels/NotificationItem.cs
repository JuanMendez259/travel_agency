using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.ViewModels;

public class NotificationItem
{
    public UserNotification Notification { get; }

    public string Message => Notification.Message ?? string.Empty;

    public string TimeText
    {
        get
        {
            var t = Notification.CreatedAt.ToLocalTime();
            return t.Date == DateTime.Today
                ? t.ToString("HH:mm")
                : t.ToString("dd/MM/yyyy HH:mm");
        }
    }

    public bool IsToday => Notification.CreatedAt.ToLocalTime().Date == DateTime.Today;

    public string DateHeaderText => IsToday ? "Hoy" : Notification.CreatedAt.ToLocalTime().ToString("dd MMMM yyyy");

    public string Icon { get; private set; } = "💬";

    public Color IconBackgroundColor { get; private set; } = Color.FromArgb("#F2F3FF");

    public Color IconTextColor { get; private set; } = Color.FromArgb("#14532D");

    public string TagText { get; private set; } = "RutaVerde · Agencia";

    public NotificationItem(UserNotification notification)
    {
        Notification = notification;
        InferTypeFromMessage();
    }

    private void InferTypeFromMessage()
    {
        var msg = Message.ToLowerInvariant();

        if (msg.Contains("reservación") || msg.Contains("reserva") || msg.Contains("confirmada") || msg.Contains("confirmado"))
        {
            Icon = "✓";
            IconBackgroundColor = Color.FromArgb("#14532D");
            IconTextColor = Colors.White;
            TagText = "RutaVerde · Agencia";
            return;
        }

        if (msg.Contains("pago") || msg.Contains("abono") || msg.Contains("comprobante"))
        {
            Icon = "💳";
            IconBackgroundColor = Color.FromArgb("#6FFBBE");
            IconTextColor = Color.FromArgb("#005236");
            TagText = "RutaVerde · Pagos";
            return;
        }

        if (msg.Contains("requisito") || msg.Contains("documentación") || msg.Contains("identificación") || msg.Contains("ine") || msg.Contains("pasaporte") || msg.Contains("check-in") || msg.Contains("abordar"))
        {
            Icon = "✓";
            IconBackgroundColor = Color.FromArgb("#006C49");
            IconTextColor = Colors.White;
            TagText = "RutaVerde · Operaciones";
            return;
        }

        if (msg.Contains("guía") || msg.Contains("guías") || msg.Contains("reunión") || msg.Contains("punto") || msg.Contains("encuentro") || msg.Contains("ruta") || msg.Contains("salida"))
        {
            Icon = "🥾";
            IconBackgroundColor = Color.FromArgb("#D8E6DC");
            IconTextColor = Color.FromArgb("#3D4A43");
            TagText = "RutaVerde · Guías y Soporte";
            return;
        }

        Icon = "📢";
        IconBackgroundColor = Color.FromArgb("#E2E7FF");
        IconTextColor = Color.FromArgb("#14532D");
        TagText = "RutaVerde · Avisos";
    }
}