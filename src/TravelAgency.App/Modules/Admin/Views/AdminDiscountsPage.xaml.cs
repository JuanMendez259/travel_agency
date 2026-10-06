using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminDiscountsPage : ContentPage
{
    private readonly ApiService _api;
    private readonly List<int?> _tripIds = new();
    private List<Trip> _trips = new();
    private Discount? _editing;

    public AdminDiscountsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;

        TypePicker.ItemsSource = new List<string> { "Porcentaje (%)", "Monto fijo (MXN)" };
        TypePicker.SelectedIndex = 0;

        StartDatePicker.Date = DateTime.Today;
        EndDatePicker.Date = DateTime.Today.AddDays(30);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadTripsAsync();
            await LoadDiscountsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar los descuentos: {ex.Message}", "OK");
        }
    }

    private async Task LoadTripsAsync()
    {
        _trips = await _api.GetAdminTripsAsync() ?? new List<Trip>();
        _tripIds.Clear();
        var labels = new List<string> { "Todos los viajes (global)" };
        _tripIds.Add(null);
        foreach (var trip in _trips.OrderByDescending(t => t.StartDate))
        {
            labels.Add($"{trip.Title} · {trip.Destination}");
            _tripIds.Add(trip.Id);
        }
        TripPicker.ItemsSource = labels;
        TripPicker.SelectedIndex = 0;
    }

    private async Task LoadDiscountsAsync()
    {
        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;
        try
        {
            var discounts = await _api.GetDiscountsAsync() ?? new List<Discount>();
            RenderDiscounts(discounts);
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private void RenderDiscounts(List<Discount> discounts)
    {
        DiscountsLayout.Children.Clear();
        CountLabel.Text = $"{discounts.Count} descuento(s)";
        EmptyLabel.IsVisible = discounts.Count == 0;

        foreach (var discount in discounts)
            DiscountsLayout.Children.Add(BuildDiscountCard(discount));
    }

    private View BuildDiscountCard(Discount discount)
    {
        var border = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            BackgroundColor = Color.FromArgb("#F2F3FF"),
            Stroke = Color.FromArgb("#E2E7FF"),
            StrokeThickness = 1,
            Padding = new Thickness(14, 12),
            Margin = new Thickness(0, 4)
        };

        var stack = new VerticalStackLayout { Spacing = 4 };

        var header = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };

        var title = new Label
        {
            Text = discount.Code,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#131B2E"),
            VerticalOptions = LayoutOptions.Center
        };

        var badges = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Start };

        badges.Children.Add(new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#EAEDFF"),
            Padding = new Thickness(8, 2),
            Content = new Label
            {
                Text = discount.IsGlobal ? "Global" : "Viaje",
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#404941")
            }
        });

        badges.Children.Add(new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            StrokeThickness = 0,
            BackgroundColor = discount.IsActive ? Color.FromArgb("#6CF8BB") : Color.FromArgb("#EAEDFF"),
            Padding = new Thickness(8, 2),
            Content = new Label
            {
                Text = discount.IsActive ? "Activo" : "Inactivo",
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = discount.IsActive ? Color.FromArgb("#003B1B") : Color.FromArgb("#404941")
            }
        });

        header.Add(title, 0, 0);
        header.Add(badges, 1, 0);
        stack.Children.Add(header);

        var valueText = discount.Type == DiscountType.Percentage
            ? $"{discount.Value:0.##}% de descuento"
            : $"{discount.Value:C} de descuento";
        stack.Children.Add(new Label { Text = valueText, FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#006C49") });

        stack.Children.Add(new Label
        {
            Text = $"Vigencia: {FormatWindow(discount)}",
            FontSize = 12,
            TextColor = Color.FromArgb("#404941")
        });

        stack.Children.Add(new Label
        {
            Text = $"Usos: {FormatUsage(discount)}",
            FontSize = 12,
            TextColor = Color.FromArgb("#404941")
        });

        if (!discount.IsGlobal)
        {
            stack.Children.Add(new Label
            {
                Text = $"Aplica a: {discount.Trip?.Title ?? $"Viaje #{discount.TripId}"}",
                FontSize = 12,
                TextColor = Color.FromArgb("#404941")
            });
        }

        var actions = new HorizontalStackLayout { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        var edit = new Button { Text = "Editar", FontSize = 12, HeightRequest = 34, CornerRadius = 8, Padding = new Thickness(12, 0) };
        var toggle = new Button { Text = discount.IsActive ? "Desactivar" : "Activar", FontSize = 12, HeightRequest = 34, CornerRadius = 8, Padding = new Thickness(12, 0) };
        var del = new Button
        {
            Text = "Eliminar",
            FontSize = 12,
            HeightRequest = 34,
            CornerRadius = 8,
            Padding = new Thickness(12, 0),
            BackgroundColor = Color.FromArgb("#BA1A1A"),
            TextColor = Colors.White
        };
        actions.Children.Add(edit);
        actions.Children.Add(toggle);
        actions.Children.Add(del);
        stack.Children.Add(actions);

        edit.Clicked += (_, _) => StartEdit(discount);
        toggle.Clicked += async (_, _) => await ToggleAsync(discount);
        del.Clicked += async (_, _) => await DeleteAsync(discount);

        border.Content = stack;
        return border;
    }

    private static string FormatWindow(Discount discount)
    {
        if (!discount.StartDate.HasValue && !discount.EndDate.HasValue) return "sin vigencia";
        if (discount.StartDate.HasValue && discount.EndDate.HasValue)
            return $"{discount.StartDate:dd/MM/yyyy} al {discount.EndDate:dd/MM/yyyy}";
        if (discount.StartDate.HasValue) return $"desde {discount.StartDate:dd/MM/yyyy}";
        return $"hasta {discount.EndDate:dd/MM/yyyy}";
    }

    private static string FormatUsage(Discount discount) =>
        discount.UsageLimit is null
            ? $"{discount.UsedCount} (ilimitado)"
            : $"{discount.UsedCount} / {discount.UsageLimit}";

    private void OnVigenciaToggled(object? sender, ToggledEventArgs e)
    {
        VigenciaFields.IsVisible = e.Value;
    }

    private void StartEdit(Discount discount)
    {
        _editing = discount;
        CodeEntry.Text = discount.Code;
        TypePicker.SelectedIndex = discount.Type == DiscountType.Percentage ? 0 : 1;
        ValueEntry.Text = discount.Value.ToString("0.##");
        UsageLimitEntry.Text = discount.UsageLimit?.ToString() ?? string.Empty;
        ActiveSwitch.IsToggled = discount.IsActive;

        var hasWindow = discount.StartDate.HasValue || discount.EndDate.HasValue;
        VigenciaSwitch.IsToggled = hasWindow;
        VigenciaFields.IsVisible = hasWindow;
        if (discount.StartDate.HasValue) StartDatePicker.Date = discount.StartDate.Value;
        if (discount.EndDate.HasValue) EndDatePicker.Date = discount.EndDate.Value;

        var tripIndex = _tripIds.IndexOf(discount.TripId);
        TripPicker.SelectedIndex = tripIndex >= 0 ? tripIndex : 0;

        FormTitleLabel.Text = "Editar descuento";
        SaveButton.Text = "Guardar cambios";
        CancelEditButton.IsVisible = true;
        FeedbackLabel.IsVisible = false;
    }

    private void ResetForm()
    {
        _editing = null;
        CodeEntry.Text = string.Empty;
        TypePicker.SelectedIndex = 0;
        ValueEntry.Text = string.Empty;
        UsageLimitEntry.Text = string.Empty;
        ActiveSwitch.IsToggled = true;
        VigenciaSwitch.IsToggled = false;
        VigenciaFields.IsVisible = false;
        StartDatePicker.Date = DateTime.Today;
        EndDatePicker.Date = DateTime.Today.AddDays(30);
        TripPicker.SelectedIndex = 0;

        FormTitleLabel.Text = "Nuevo descuento";
        SaveButton.Text = "Guardar descuento";
        CancelEditButton.IsVisible = false;
        FeedbackLabel.IsVisible = false;
    }

    private void OnCancelEditClicked(object? sender, EventArgs e) => ResetForm();

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var code = CodeEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            await DisplayAlertAsync("Falta el código", "Escribe un código de descuento.", "OK");
            return;
        }

        if (!decimal.TryParse(ValueEntry.Text, out var value) || value <= 0)
        {
            await DisplayAlertAsync("Valor inválido", "Escribe un valor mayor a 0.", "OK");
            return;
        }

        var type = TypePicker.SelectedIndex == 1 ? DiscountType.Amount : DiscountType.Percentage;
        if (type == DiscountType.Percentage && value > 100)
        {
            await DisplayAlertAsync("Valor inválido", "El porcentaje no puede ser mayor a 100.", "OK");
            return;
        }

        int? usageLimit = null;
        if (!string.IsNullOrWhiteSpace(UsageLimitEntry.Text))
        {
            if (!int.TryParse(UsageLimitEntry.Text, out var limit) || limit < 1)
            {
                await DisplayAlertAsync("Límite inválido", "El límite de uso debe ser al menos 1 (o déjalo vacío).", "OK");
                return;
            }
            usageLimit = limit;
        }

        DateTime? start = null, end = null;
        if (VigenciaSwitch.IsToggled)
        {
            start = StartDatePicker.Date?.Date;
            end = EndDatePicker.Date?.Date;
            if (start.HasValue && end.HasValue && end.Value < start.Value)
            {
                await DisplayAlertAsync("Fechas inválidas", "La fecha de fin no puede ser anterior a la de inicio.", "OK");
                return;
            }
        }

        var tripIndex = TripPicker.SelectedIndex;
        int? tripId = tripIndex >= 0 && tripIndex < _tripIds.Count ? _tripIds[tripIndex] : null;

        var discount = new Discount
        {
            Id = _editing?.Id ?? 0,
            Code = code,
            Type = type,
            Value = value,
            StartDate = start,
            EndDate = end,
            UsageLimit = usageLimit,
            IsActive = ActiveSwitch.IsToggled,
            TripId = tripId
        };

        SaveButton.IsEnabled = false;
        try
        {
            if (_editing is null)
                await _api.CreateDiscountAsync(discount);
            else
                await _api.UpdateDiscountAsync(discount);

            ResetForm();
            await LoadDiscountsAsync();
            FeedbackLabel.IsVisible = true;
            FeedbackLabel.TextColor = Color.FromArgb("#006C49");
            FeedbackLabel.Text = "Descuento guardado.";
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async Task ToggleAsync(Discount discount)
    {
        var previous = discount.IsActive;
        discount.IsActive = !discount.IsActive;
        try
        {
            await _api.UpdateDiscountAsync(discount);
            await LoadDiscountsAsync();
        }
        catch (Exception ex)
        {
            discount.IsActive = previous;
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async Task DeleteAsync(Discount discount)
    {
        var ok = await DisplayAlertAsync("Eliminar", $"¿Eliminar el descuento \"{discount.Code}\"?", "Sí", "No");
        if (!ok) return;

        try
        {
            await _api.DeleteDiscountAsync(discount.Id);
            if (_editing?.Id == discount.Id) ResetForm();
            await LoadDiscountsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}
