using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminCoordinadoresPage : ContentPage
{
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    private readonly ApiService _api;

    public AdminCoordinadoresPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadCoordinatorsAsync();
    }

    private async Task LoadCoordinatorsAsync()
    {
        LoadingIndicator.IsRunning = true;
        try
        {
            var users = await _api.GetUsersAsync();
            var coordinators = (users ?? new List<User>())
                .Where(u => u.Role == UserRole.Coordinador)
                .OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            CountLabel.Text = coordinators.Count == 1
                ? "1 coordinador con acceso al sistema"
                : $"{coordinators.Count} coordinadores con acceso al sistema";

            EmptyLabel.IsVisible = coordinators.Count == 0;
            CoordinatorsLayout.Clear();

            foreach (var coordinator in coordinators)
            {
                CoordinatorsLayout.Add(BuildCoordinatorRow(coordinator));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
        }
    }

    private static View BuildCoordinatorRow(User coordinator)
    {
        var phone = string.IsNullOrWhiteSpace(coordinator.Phone) ? "Sin teléfono" : coordinator.Phone;

        return new Border
        {
            Padding = 12,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
            BackgroundColor = Color.FromArgb("#F7FAFC"),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = coordinator.Name ?? "(sin nombre)",
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold
                    },
                    new Label { Text = coordinator.Email ?? string.Empty, FontSize = 13 },
                    new Label
                    {
                        Text = $"{phone} · Alta {coordinator.CreatedAt.ToLocalTime():dd/MM/yyyy}",
                        FontSize = 12,
                        TextColor = Colors.Gray
                    }
                }
            }
        };
    }

    private async void OnGeneratePasswordClicked(object? sender, EventArgs e)
    {
        PasswordEntry.Text = GeneratePassword();
        await DisplayAlertAsync("Contraseña generada",
            "Se generó una contraseña temporal. Puedes copiarla o escribir otra.", "OK");
    }

    private async void OnRegisterClicked(object? sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim();
        var email = EmailEntry.Text?.Trim();
        var phone = PhoneEntry.Text?.Trim();
        var password = PasswordEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            await ShowFeedback("Escribe el nombre del coordinador.", isError: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            await ShowFeedback("Escribe un correo válido.", isError: true);
            return;
        }

        if (!string.IsNullOrWhiteSpace(password) && password.Length < 6)
        {
            await ShowFeedback("La contraseña debe tener al menos 6 caracteres.", isError: true);
            return;
        }

        RegisterButton.IsEnabled = false;
        LoadingIndicator.IsRunning = true;
        try
        {
            var created = await _api.CreateCoordinatorAsync(name, email, phone,
                string.IsNullOrWhiteSpace(password) ? null : password);

            ClearForm();

            await LoadCoordinatorsAsync();

            if (created is not null)
            {
                await DisplayAlertAsync("Coordinador registrado",
                    $"{created.Name} ({created.Email})\n\n"
                    + $"Contraseña temporal: {created.TemporaryPassword}\n\n"
                    + "Anótala y compártela por un canal seguro. No se vuelve a mostrar.",
                    "Entendido");
            }
        }
        catch (Exception ex)
        {
            await ShowFeedback(ex.Message, isError: true);
        }
        finally
        {
            RegisterButton.IsEnabled = true;
            LoadingIndicator.IsRunning = false;
        }
    }

    private async Task ShowFeedback(string message, bool isError)
    {
        FeedbackLabel.Text = message;
        FeedbackLabel.TextColor = isError ? Color.FromArgb("#C53030") : Color.FromArgb("#2F855A");
        FeedbackLabel.IsVisible = true;
        await Task.CompletedTask;
    }

    private void ClearForm()
    {
        NameEntry.Text = string.Empty;
        EmailEntry.Text = string.Empty;
        PhoneEntry.Text = string.Empty;
        PasswordEntry.Text = string.Empty;
        FeedbackLabel.IsVisible = false;
    }

    private static string GeneratePassword()
    {
        var chars = new char[10];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = PasswordAlphabet[Random.Shared.Next(PasswordAlphabet.Length)];
        return new string(chars);
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}
