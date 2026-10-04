using TravelAgency.App.Services;
using TravelAgency.Shared.Models;
using System.Security.Cryptography;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminCoordinadoresPage : ContentPage
{
    private readonly ApiService _api;

    public AdminCoordinadoresPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadCoordinatorsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar los coordinadores: {ex.Message}", "OK");
        }
    }

    private async Task LoadCoordinatorsAsync(bool showLoading = true)
    {
        if (showLoading)
        {
            LoadingIndicator.IsRunning = true;
        }

        try
        {
            var users = await _api.GetUsersAsync();
            var list = (users ?? new List<User>())
                .Where(u => u.Role == UserRole.Coordinador)
                .OrderBy(u => u.Name)
                .ToList();

            CoordinatorsLayout.Children.Clear();

            if (list.Count == 0)
            {
                EmptyLabel.IsVisible = true;
                CountLabel.Text = "0 coordinadores";
            }
            else
            {
                EmptyLabel.IsVisible = false;
                CountLabel.Text = $"{list.Count} coordinador(es)";

                foreach (var user in list)
                {
                    CoordinatorsLayout.Children.Add(BuildCoordinatorRow(user));
                }
            }
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
        }
    }

    private View BuildCoordinatorRow(User user)
    {
        var name = string.IsNullOrWhiteSpace(user.Name) ? "Sin nombre" : user.Name;
        var email = string.IsNullOrWhiteSpace(user.Email) ? "Sin correo" : user.Email;

        var border = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            BackgroundColor = Color.FromArgb("#F2F3FF"),
            Stroke = Color.FromArgb("#E2E7FF"),
            StrokeThickness = 1,
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 4)
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };

        var nameLabel = new Label
        {
            Text = name,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#131B2E")
        };

        var emailLabel = new Label
        {
            Text = email,
            FontSize = 12,
            TextColor = Color.FromArgb("#404941")
        };

        var roleLabel = new Label
        {
            Text = "Coordinador",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#006C49")
        };

        Grid.SetRow(emailLabel, 1);
        Grid.SetRowSpan(roleLabel, 2);
        Grid.SetColumn(roleLabel, 1);
        Grid.SetRow(roleLabel, 0);

        grid.Add(nameLabel);
        grid.Add(emailLabel);
        grid.Add(roleLabel);

        border.Content = grid;
        return border;
    }

    private void OnGeneratePasswordClicked(object? sender, EventArgs e)
    {
        PasswordEntry.Text = GenerateRandomPassword();
    }

    private static string GenerateRandomPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var random = RandomNumberGenerator.Create();
        var buffer = new byte[10];
        random.GetBytes(buffer);

        var result = new System.Text.StringBuilder(10);
        for (int i = 0; i < 10; i++)
        {
            result.Append(chars[buffer[i] % chars.Length]);
        }

        return result.ToString();
    }

    private async void OnRegisterClicked(object? sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim() ?? string.Empty;
        var email = EmailEntry.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var phone = PhoneEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlertAsync("Datos incompletos", "Indica el nombre completo del coordinador.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            await DisplayAlertAsync("Datos incompletos", "Indica un correo electrónico válido.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            password = GenerateRandomPassword();
            PasswordEntry.Text = password;
        }
        else if (password.Length < 6)
        {
            await DisplayAlertAsync("Datos incompletos", "La contraseña temporal debe tener al menos 6 caracteres.", "OK");
            return;
        }

        RegisterButton.IsEnabled = false;
        FeedbackLabel.IsVisible = true;
        FeedbackLabel.Text = "Registrando coordinador...";
        FeedbackLabel.TextColor = Color.FromArgb("#404941");

        try
        {
            var user = await _api.CreateCoordinatorAsync(name, email, phone, password);
            if (user is null)
            {
                FeedbackLabel.Text = "No fue posible registrar al coordinador.";
                FeedbackLabel.TextColor = Color.FromArgb("#BA1A1A");
                return;
            }

            FeedbackLabel.Text = $"Coordinador registrado: {user.Email}. Contraseña temporal: {password}";
            FeedbackLabel.TextColor = Color.FromArgb("#006C49");

            NameEntry.Text = string.Empty;
            EmailEntry.Text = string.Empty;
            PhoneEntry.Text = string.Empty;
            PasswordEntry.Text = string.Empty;

            await LoadCoordinatorsAsync(showLoading: false);

            await DisplayAlertAsync("Coordinador registrado",
                $"Se registró {user.Name} ({user.Email}) con rol Coordinador. La contraseña temporal es {password}. Compártela por un canal seguro.",
                "OK");
        }
        catch (Exception ex)
        {
            FeedbackLabel.Text = ex.Message;
            FeedbackLabel.TextColor = Color.FromArgb("#BA1A1A");
        }
        finally
        {
            RegisterButton.IsEnabled = true;
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}
