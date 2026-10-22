using TravelAgency.App.Modules.Admin;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;
using System.Security.Cryptography;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminCoordinadoresPage : ContentPage
{
    private readonly ApiService _api;
    private int? _editingId;

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
        var phone = string.IsNullOrWhiteSpace(user.Phone) ? "Sin teléfono" : user.Phone;

        var avatar = new Border
        {
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 22 },
            BackgroundColor = Color.FromArgb("#E2E7FF"),
            Content = new Label
            {
                Text = AdminDisplay.Initials(name),
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#003B1B"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var info = new VerticalStackLayout { Spacing = 2 };
        info.Add(new Label { Text = name, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#131B2E") });
        info.Add(new Label { Text = email, FontSize = 12, TextColor = Color.FromArgb("#404941") });
        info.Add(new Label { Text = phone, FontSize = 12, TextColor = Color.FromArgb("#404941") });

        var roleBadge = new Border
        {
            Padding = new Thickness(8, 3),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 100 },
            BackgroundColor = Color.FromArgb("#E6F4EA"),
            VerticalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = "Coordinador",
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#14532D")
            }
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        header.Add(avatar, 0, 0);
        header.Add(info, 1, 0);
        header.Add(roleBadge, 2, 0);

        var editButton = new Button
        {
            Text = "Editar",
            FontSize = 12,
            MinimumHeightRequest = 44,
            CornerRadius = 14,
            Padding = new Thickness(4, 0),
            BackgroundColor = Color.FromArgb("#003B1B"),
            TextColor = Colors.White
        };
        editButton.Clicked += (_, __) => EnterEditMode(user);

        var resetButton = new Button
        {
            Text = "Resetear",
            FontSize = 12,
            MinimumHeightRequest = 44,
            CornerRadius = 14,
            Padding = new Thickness(4, 0),
            BackgroundColor = Color.FromArgb("#DAE2FD"),
            TextColor = Color.FromArgb("#131B2E")
        };
        resetButton.Clicked += async (_, __) => await OnResetPasswordAsync(user);

        var deleteButton = new Button
        {
            Text = "Eliminar",
            FontSize = 12,
            MinimumHeightRequest = 44,
            CornerRadius = 14,
            Padding = new Thickness(4, 0),
            BackgroundColor = Color.FromArgb("#BA1A1A"),
            TextColor = Colors.White
        };
        deleteButton.Clicked += async (_, __) => await OnDeleteAsync(user);

        var actions = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 6,
            Margin = new Thickness(0, 10, 0, 0)
        };
        actions.Add(editButton, 0, 0);
        actions.Add(resetButton, 1, 0);
        actions.Add(deleteButton, 2, 0);

        var content = new VerticalStackLayout { Spacing = 0 };
        content.Add(header);
        content.Add(actions);

        return new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            BackgroundColor = Color.FromArgb("#FFFFFF"),
            Stroke = Color.FromArgb("#DAE2FD"),
            StrokeThickness = 1,
            Padding = new Thickness(14),
            Margin = new Thickness(0, 2),
            Content = content
        };
    }

    private void EnterEditMode(User user)
    {
        _editingId = user.Id;
        NameEntry.Text = user.Name ?? string.Empty;
        EmailEntry.Text = user.Email ?? string.Empty;
        PhoneEntry.Text = user.Phone ?? string.Empty;
        PasswordEntry.Text = string.Empty;
        PasswordFieldsLayout.IsVisible = false;
        GeneratePasswordButton.IsVisible = false;
        FormTitleLabel.Text = "Editar coordinador";
        RegisterButton.Text = "Guardar cambios";
        CancelEditButton.IsVisible = true;
        FeedbackLabel.IsVisible = false;
    }

    private void OnCancelEditClicked(object? sender, EventArgs e) => SetCreateMode();

    private void SetCreateMode()
    {
        _editingId = null;
        NameEntry.Text = string.Empty;
        EmailEntry.Text = string.Empty;
        PhoneEntry.Text = string.Empty;
        PasswordEntry.Text = string.Empty;
        PasswordFieldsLayout.IsVisible = true;
        GeneratePasswordButton.IsVisible = true;
        FormTitleLabel.Text = "Registrar coordinador";
        RegisterButton.Text = "Registrar coordinador";
        CancelEditButton.IsVisible = false;
        FeedbackLabel.IsVisible = false;
    }

    private async Task OnResetPasswordAsync(User user)
    {
        var confirm = await DisplayAlertAsync("Resetear contraseña",
            $"¿Generar una nueva contraseña temporal para {user.Name} ({user.Email})?", "Sí", "No");
        if (!confirm) return;

        try
        {
            var password = await _api.ResetCoordinatorPasswordAsync(user.Id);
            if (string.IsNullOrWhiteSpace(password))
            {
                await DisplayAlertAsync("Error", "No se pudo resetear la contraseña.", "OK");
                return;
            }

            await DisplayAlertAsync("Contraseña temporal",
                $"Nueva contraseña temporal para {user.Email}: {password}\n\nCompártela por un canal seguro; no se vuelve a mostrar.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async Task OnDeleteAsync(User user)
    {
        var confirm = await DisplayAlertAsync("Eliminar coordinador",
            $"¿Eliminar a {user.Name} ({user.Email})? Esta acción no se puede deshacer.", "Eliminar", "Cancelar");
        if (!confirm) return;

        try
        {
            await _api.DeleteCoordinatorAsync(user.Id);
            if (_editingId == user.Id) SetCreateMode();
            await LoadCoordinatorsAsync(showLoading: false);
            await DisplayAlertAsync("Listo", "Coordinador eliminado.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo eliminar", ex.Message, "OK");
        }
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

        if (_editingId is int editId)
        {
            RegisterButton.IsEnabled = false;
            FeedbackLabel.IsVisible = false;
            try
            {
                await _api.UpdateCoordinatorAsync(editId, name, email, phone);
                SetCreateMode();
                await LoadCoordinatorsAsync(showLoading: false);
                await DisplayAlertAsync("Coordinador actualizado", "Los cambios se guardaron correctamente.", "OK");
            }
            catch (Exception ex)
            {
                FeedbackLabel.IsVisible = true;
                FeedbackLabel.Text = ex.Message;
                FeedbackLabel.TextColor = Color.FromArgb("#BA1A1A");
            }
            finally
            {
                RegisterButton.IsEnabled = true;
            }
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
