using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminTripExpensesPage : ContentPage
{
    private readonly ApiService _api;
    private readonly List<int> _tripIds = new();
    private List<TripExpense> _expenses = new();
    private TripExpense? _editing;
    private bool _suppressTripChanged;
    private bool _sanitizingAmount;

    public AdminTripExpensesPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadTripsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar los viajes: {ex.Message}", "OK");
        }
    }

    private async Task LoadTripsAsync()
    {
        var trips = await _api.GetAdminTripsAsync() ?? new List<Trip>();

        _tripIds.Clear();
        var labels = new List<string>();
        foreach (var trip in trips.OrderByDescending(t => t.StartDate))
        {
            labels.Add($"{trip.Title} · {trip.Destination}");
            _tripIds.Add(trip.Id);
        }

        _suppressTripChanged = true;
        TripPicker.ItemsSource = labels;
        TripPicker.SelectedIndex = labels.Count > 0 ? 0 : -1;
        _suppressTripChanged = false;

        UpdateTripTitle();
        await LoadExpensesAsync();
    }

    private int? SelectedTripId =>
        TripPicker.SelectedIndex >= 0 && TripPicker.SelectedIndex < _tripIds.Count
            ? _tripIds[TripPicker.SelectedIndex]
            : null;

    private void UpdateTripTitle()
    {
        var source = TripPicker.ItemsSource;
        var index = TripPicker.SelectedIndex;
        TripTitleLabel.Text = source is not null && index >= 0 && index < source.Count
            ? source[index]?.ToString() ?? "—"
            : "—";
    }

    private async void OnTripChanged(object? sender, EventArgs e)
    {
        if (_suppressTripChanged) return;
        UpdateTripTitle();
        ResetForm();
        await LoadExpensesAsync();
    }

    // Solo permite digitos y un separador decimal (se normaliza a ".").
    private void OnAmountChanged(object? sender, TextChangedEventArgs e)
    {
        if (_sanitizingAmount) return;

        var input = e.NewTextValue ?? string.Empty;
        var sanitized = SanitizeAmount(input);
        if (sanitized == input) return;

        _sanitizingAmount = true;
        AmountEntry.Text = sanitized;
        _sanitizingAmount = false;
        AmountEntry.CursorPosition = sanitized.Length;
    }

    private static string SanitizeAmount(string text)
    {
        var builder = new System.Text.StringBuilder();
        var hasSeparator = false;
        foreach (var ch in text)
        {
            if (char.IsDigit(ch))
            {
                builder.Append(ch);
            }
            else if ((ch == '.' || ch == ',') && !hasSeparator)
            {
                builder.Append('.');
                hasSeparator = true;
            }
        }
        return builder.ToString();
    }

    private async Task LoadExpensesAsync()
    {
        var tripId = SelectedTripId;
        if (tripId is null)
        {
            RenderExpenses(new List<TripExpense>());
            return;
        }

        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;
        try
        {
            _expenses = await _api.GetTripExpensesAsync(tripId.Value) ?? new List<TripExpense>();
            RenderExpenses(_expenses);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar los gastos: {ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private void RenderExpenses(List<TripExpense> expenses)
    {
        ExpensesLayout.Children.Clear();
        CountLabel.Text = $"{expenses.Count} gasto(s)";
        EmptyLabel.IsVisible = expenses.Count == 0;

        foreach (var expense in expenses)
            ExpensesLayout.Children.Add(BuildExpenseCard(expense));

        TotalLabel.Text = $"$ {expenses.Sum(e => e.Amount):0.00}";
    }

    private View BuildExpenseCard(TripExpense expense)
    {
        var border = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            BackgroundColor = Color.FromArgb("#FFFFFF"),
            Stroke = Color.FromArgb("#DAE2FD"),
            StrokeThickness = 1,
            Padding = new Thickness(14),
            Margin = new Thickness(0, 2)
        };

        var stack = new VerticalStackLayout { Spacing = 4 };

        var header = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };

        header.Add(new Label
        {
            Text = expense.Concept,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#131B2E"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.WordWrap
        }, 0, 0);

        header.Add(new Label
        {
            Text = $"$ {expense.Amount:0.00}",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#006C49"),
            VerticalOptions = LayoutOptions.Center
        }, 1, 0);

        stack.Children.Add(header);

        stack.Children.Add(new Label
        {
            Text = $"Registrado: {expense.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}",
            FontSize = 12,
            TextColor = Color.FromArgb("#404941")
        });

        var actions = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var edit = new Button
        {
            Text = "Editar",
            FontSize = 12,
            MinimumHeightRequest = 44,
            CornerRadius = 14,
            Padding = new Thickness(4, 0),
            BackgroundColor = Color.FromArgb("#003B1B"),
            TextColor = Colors.White
        };
        var del = new Button
        {
            Text = "Eliminar",
            FontSize = 12,
            MinimumHeightRequest = 44,
            CornerRadius = 14,
            Padding = new Thickness(4, 0),
            BackgroundColor = Color.FromArgb("#BA1A1A"),
            TextColor = Colors.White
        };

        edit.Clicked += (_, _) => StartEdit(expense);
        del.Clicked += async (_, _) => await DeleteAsync(expense);

        actions.Add(edit, 0, 0);
        actions.Add(del, 1, 0);
        stack.Children.Add(actions);

        border.Content = stack;
        return border;
    }

    private void StartEdit(TripExpense expense)
    {
        _editing = expense;
        ConceptEntry.Text = expense.Concept;
        AmountEntry.Text = expense.Amount.ToString("0.00");

        FormTitleLabel.Text = "Editar gasto o inversión";
        AddButton.Text = "Guardar";
        CancelEditButton.IsVisible = true;
        FeedbackLabel.IsVisible = false;
    }

    private void ResetForm()
    {
        _editing = null;
        ConceptEntry.Text = string.Empty;
        AmountEntry.Text = string.Empty;

        FormTitleLabel.Text = "Agregar gasto o inversión";
        AddButton.Text = "Agregar gasto";
        CancelEditButton.IsVisible = false;
        FeedbackLabel.IsVisible = false;
    }

    private void OnCancelEditClicked(object? sender, EventArgs e) => ResetForm();

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var tripId = SelectedTripId;
        if (tripId is null)
        {
            await DisplayAlertAsync("Elige un viaje", "Selecciona un viaje para registrar el gasto.", "OK");
            return;
        }

        var concept = ConceptEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(concept))
        {
            await DisplayAlertAsync("Falta el concepto", "Escribe el concepto o descripcion del gasto.", "OK");
            return;
        }

        var rawAmount = (AmountEntry.Text ?? string.Empty).Replace("$", string.Empty).Replace(",", string.Empty).Trim();
        if (!decimal.TryParse(rawAmount, out var amount) || amount <= 0)
        {
            await DisplayAlertAsync("Cantidad inválida", "Escribe una cantidad mayor a 0.", "OK");
            return;
        }

        AddButton.IsEnabled = false;
        try
        {
            if (_editing is null)
                await _api.CreateTripExpenseAsync(tripId.Value, concept, amount);
            else
                await _api.UpdateTripExpenseAsync(_editing.Id, concept, amount);

            ResetForm();
            await LoadExpensesAsync();
            FeedbackLabel.IsVisible = true;
            FeedbackLabel.TextColor = Color.FromArgb("#006C49");
            FeedbackLabel.Text = "Gasto guardado.";
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            AddButton.IsEnabled = true;
        }
    }

    private async Task DeleteAsync(TripExpense expense)
    {
        var ok = await DisplayAlertAsync("Eliminar", $"¿Eliminar el gasto \"{expense.Concept}\"?", "Sí", "No");
        if (!ok) return;

        try
        {
            await _api.DeleteTripExpenseAsync(expense.Id);
            if (_editing?.Id == expense.Id) ResetForm();
            await LoadExpensesAsync();
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
