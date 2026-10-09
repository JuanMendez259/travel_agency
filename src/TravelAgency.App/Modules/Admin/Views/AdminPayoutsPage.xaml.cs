using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminPayoutsPage : ContentPage
{
    private readonly ApiService _api;
    private bool _busy;

    public AdminPayoutsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadPayoutsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar los reembolsos: {ex.Message}", "OK");
        }
    }

    private async Task LoadPayoutsAsync()
    {
        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;
        ErrorLabel.IsVisible = false;

        try
        {
            var payouts = await _api.GetPayoutRequestsAsync() ?? new List<PayoutRequest>();
            var users = await _api.GetUsersAsync() ?? new List<User>();
            var names = users
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().Name) ? g.First().Email : g.First().Name);

            RenderPayouts(payouts, names);
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = $"No se pudieron cargar los reembolsos: {ex.Message}";
            ErrorLabel.IsVisible = true;
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private void RenderPayouts(List<PayoutRequest> payouts, Dictionary<int, string?> names)
    {
        PayoutsLayout.Children.Clear();

        var ordered = payouts
            .OrderBy(p => p.Status == PayoutStatus.Pending ? 0 : 1)
            .ThenByDescending(p => p.CreatedAt)
            .ToList();

        var pending = payouts.Count(p => p.Status == PayoutStatus.Pending);
        CountLabel.Text = payouts.Count == 0
            ? "Sin solicitudes"
            : $"{payouts.Count} solicitud(es) · {pending} pendiente(s)";

        EmptyLabel.IsVisible = payouts.Count == 0;

        foreach (var payout in ordered)
            PayoutsLayout.Children.Add(BuildPayoutCard(payout, names));
    }

    private View BuildPayoutCard(PayoutRequest payout, Dictionary<int, string?> names)
    {
        var userName = names.TryGetValue(payout.UserId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name!
            : $"Usuario #{payout.UserId}";

        var (statusText, statusBg, statusColor) = payout.Status switch
        {
            PayoutStatus.Paid => ("Pagado", Color.FromArgb("#6CF8BB"), Color.FromArgb("#003B1B")),
            PayoutStatus.Rejected => ("Rechazado", Color.FromArgb("#FFDAD6"), Color.FromArgb("#93000A")),
            _ => ("Pendiente", Color.FromArgb("#E2E7FF"), Color.FromArgb("#404941"))
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        var info = new VerticalStackLayout { Spacing = 2 };
        info.Add(new Label
        {
            Text = userName,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#131B2E")
        });
        info.Add(new Label
        {
            Text = payout.Amount.ToString("C"),
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#006C49")
        });
        info.Add(new Label
        {
            Text = $"Solicitado: {payout.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}",
            FontSize = 12,
            TextColor = Color.FromArgb("#404941")
        });
        if (!string.IsNullOrWhiteSpace(payout.Note))
            info.Add(new Label { Text = payout.Note, FontSize = 12, TextColor = Color.FromArgb("#404941") });
        if (payout.ResolvedAt.HasValue)
            info.Add(new Label
            {
                Text = $"Resuelto: {payout.ResolvedAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}",
                FontSize = 12,
                TextColor = Color.FromArgb("#404941")
            });

        var badge = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            StrokeThickness = 0,
            BackgroundColor = statusBg,
            Padding = new Thickness(8, 2),
            VerticalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = statusText,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = statusColor
            }
        };

        header.Add(info, 0, 0);
        header.Add(badge, 1, 0);

        var content = new VerticalStackLayout { Spacing = 0 };
        content.Add(header);

        if (payout.Status == PayoutStatus.Pending)
        {
            var payButton = new Button
            {
                Text = "Marcar como pagado",
                FontSize = 12,
                HeightRequest = 34,
                CornerRadius = 8,
                Padding = new Thickness(12, 0),
                BackgroundColor = Color.FromArgb("#003B1B"),
                TextColor = Colors.White
            };
            payButton.Clicked += async (_, _) => await ResolveAsync(payout, approve: true);

            var rejectButton = new Button
            {
                Text = "Rechazar",
                FontSize = 12,
                HeightRequest = 34,
                CornerRadius = 8,
                Padding = new Thickness(12, 0),
                BackgroundColor = Color.FromArgb("#BA1A1A"),
                TextColor = Colors.White
            };
            rejectButton.Clicked += async (_, _) => await ResolveAsync(payout, approve: false);

            var actions = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            actions.Add(payButton);
            actions.Add(rejectButton);
            content.Add(actions);
        }

        return new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            BackgroundColor = Color.FromArgb("#F2F3FF"),
            Stroke = Color.FromArgb("#E2E7FF"),
            StrokeThickness = 1,
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 4),
            Content = content
        };
    }

    private async Task ResolveAsync(PayoutRequest payout, bool approve)
    {
        if (_busy) return;

        var title = approve ? "Marcar como pagado" : "Rechazar reembolso";
        var message = approve
            ? $"Confirma que entregaste {payout.Amount:C} a este cliente. Se descontara de su saldo."
            : $"Se rechazara la solicitud de {payout.Amount:C}. El saldo seguira disponible para el cliente.";
        var accept = approve ? "Si, pagado" : "Si, rechazar";

        var confirm = await DisplayAlertAsync(title, message, accept, "Cancelar");
        if (!confirm) return;

        _busy = true;
        try
        {
            await _api.ResolvePayoutRequestAsync(payout.Id, approve);
            await LoadPayoutsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo resolver", ex.Message, "OK");
        }
        finally
        {
            _busy = false;
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesion", "¿Deseas salir de la cuenta de administrador?", "Si", "No");
        if (confirm) App.GoToLogin();
    }
}
