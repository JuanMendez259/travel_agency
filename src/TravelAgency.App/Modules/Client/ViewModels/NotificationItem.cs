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

    public NotificationItem(UserNotification notification)
    {
        Notification = notification;
    }
}