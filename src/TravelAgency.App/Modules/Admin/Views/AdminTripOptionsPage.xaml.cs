using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminTripOptionsPage : ContentPage
{
    private readonly ApiService _api;
    private Trip? _trip;
    private TripOption? _editingOption;

    public string TripId { get; set; } = string.Empty;

    public AdminTripOptionsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadTripAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar las opciones: {ex.Message}", "OK");
        }
    }

    private async Task LoadTripAsync()
    {
        if (!int.TryParse(TripId, out var id)) return;

        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;
        try
        {
            var trips = await _api.GetAdminTripsAsync();
            _trip = trips?.FirstOrDefault(t => t.Id == id);
            if (_trip is null)
            {
                await DisplayAlertAsync("Error", "No se encontró el viaje.", "OK");
                return;
            }

            Title = "Opciones del viaje";
            TripNameLabel.Text = _trip.Title;
            AvailabilityLabel.Text = _trip.IncludesHotel
                ? $"Cupo según hotel: {_trip.BookableCapacity} lugares · disponibles {Math.Max(0, _trip.AvailableSeats)}"
                : $"Cupo según transporte: {_trip.BookableCapacity} asientos · disponibles {Math.Max(0, _trip.AvailableSeats)}";
            HotelLabel.Text = _trip.IncludesHotel
                ? $"Hotel: {(string.IsNullOrWhiteSpace(_trip.HotelName) ? "(sin nombre)" : _trip.HotelName)}"
                : "Este viaje no incluye hotel; la disponibilidad es la capacidad del transporte.";
            HotelLabel.IsVisible = true;

            RenderOptions();
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private void RenderOptions()
    {
        OptionsLayout.Children.Clear();

        var options = _trip?.Options?.OrderBy(o => o.Order).ToList() ?? new List<TripOption>();
        CountLabel.Text = $"{options.Count} opción(es)";
        EmptyLabel.IsVisible = options.Count == 0;

        foreach (var opt in options)
            OptionsLayout.Children.Add(BuildOptionCard(opt));
    }

    private View BuildOptionCard(TripOption opt)
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
            Text = opt.Name ?? "(sin nombre)",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#131B2E"),
            VerticalOptions = LayoutOptions.Center
        };

        var badges = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Start };
        if (opt.IsBase)
        {
            badges.Children.Add(new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb("#EAEDFF"),
                Padding = new Thickness(8, 2),
                Content = new Label
                {
                    Text = "Base",
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#404941")
                }
            });
        }

        var badge = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            StrokeThickness = 0,
            BackgroundColor = opt.IsActive ? Color.FromArgb("#6CF8BB") : Color.FromArgb("#EAEDFF"),
            Padding = new Thickness(8, 2),
            Content = new Label
            {
                Text = opt.IsActive ? "Activa" : "Inactiva",
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = opt.IsActive ? Color.FromArgb("#003B1B") : Color.FromArgb("#404941")
            }
        };
        badges.Children.Add(badge);

        header.Add(title, 0, 0);
        header.Add(badges, 1, 0);
        stack.Children.Add(header);

        var childText = opt.PriceChild.HasValue ? $"{opt.PriceChild.Value:C} niño" : "niño paga como adulto";
        stack.Children.Add(new Label
        {
            Text = $"{opt.PriceAdult:C} adulto · {childText}",
            FontSize = 13,
            TextColor = Color.FromArgb("#404941")
        });

        foreach (var line in opt.BenefitLines)
        {
            stack.Children.Add(new Label
            {
                Text = $"• {line}",
                FontSize = 12,
                TextColor = Color.FromArgb("#404941")
            });
        }

        var actions = new HorizontalStackLayout { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        var edit = new Button { Text = "Editar", FontSize = 12, HeightRequest = 34, CornerRadius = 8, Padding = new Thickness(12, 0) };
        var toggle = new Button { Text = opt.IsActive ? "Desactivar" : "Activar", FontSize = 12, HeightRequest = 34, CornerRadius = 8, Padding = new Thickness(12, 0) };
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
        if (opt.IsBase)
        {
            del.IsEnabled = false;
            del.Text = "Base fija";
        }
        stack.Children.Add(actions);

        edit.Clicked += (_, __) => StartEdit(opt);
        toggle.Clicked += async (_, __) => await ToggleAsync(opt);
        del.Clicked += async (_, __) => await DeleteAsync(opt);

        border.Content = stack;
        return border;
    }

    private void StartEdit(TripOption opt)
    {
        _editingOption = opt;
        OptionNameEntry.Text = opt.Name;
        PriceAdultEntry.Text = opt.PriceAdult.ToString("0.##");
        PriceChildEntry.Text = opt.PriceChild?.ToString("0.##");
        BenefitsEditor.Text = opt.Benefits;
        ActiveSwitch.IsToggled = opt.IsActive;

        FormTitleLabel.Text = "Editar opción";
        SaveButton.Text = "Guardar cambios";
        CancelEditButton.IsVisible = true;
        FeedbackLabel.IsVisible = false;
    }

    private void ResetForm()
    {
        _editingOption = null;
        OptionNameEntry.Text = string.Empty;
        PriceAdultEntry.Text = string.Empty;
        PriceChildEntry.Text = string.Empty;
        BenefitsEditor.Text = string.Empty;
        ActiveSwitch.IsToggled = true;
        FormTitleLabel.Text = "Nueva opción";
        SaveButton.Text = "Guardar opción";
        CancelEditButton.IsVisible = false;
        FeedbackLabel.IsVisible = false;
    }

    private void OnCancelEditClicked(object? sender, EventArgs e) => ResetForm();

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_trip is null) return;

        var name = OptionNameEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlertAsync("Falta el nombre", "Escribe el nombre de la opción.", "OK");
            return;
        }

        if (!decimal.TryParse(PriceAdultEntry.Text, out var priceAdult) || priceAdult < 0)
        {
            await DisplayAlertAsync("Precio inválido", "Escribe el precio adulto en MXN (sin signos).", "OK");
            return;
        }

        decimal? priceChild = null;
        if (!string.IsNullOrWhiteSpace(PriceChildEntry.Text))
        {
            if (!decimal.TryParse(PriceChildEntry.Text, out var child) || child < 0)
            {
                await DisplayAlertAsync("Precio inválido", "El precio de niño debe ser un número válido o quedar vacío.", "OK");
                return;
            }
            priceChild = child;
        }

        var benefits = string.IsNullOrWhiteSpace(BenefitsEditor.Text) ? null : BenefitsEditor.Text.Trim();

        SaveButton.IsEnabled = false;
        try
        {
            if (_editingOption is null)
            {
                var opt = new TripOption
                {
                    TripId = _trip.Id,
                    Name = name,
                    PriceAdult = priceAdult,
                    PriceChild = priceChild,
                    Benefits = benefits,
                    IsActive = ActiveSwitch.IsToggled,
                    Order = (_trip.Options?.Count ?? 0) + 1
                };
                await _api.CreateTripOptionAsync(_trip.Id, opt);
            }
            else
            {
                _editingOption.Name = name;
                _editingOption.PriceAdult = priceAdult;
                _editingOption.PriceChild = priceChild;
                _editingOption.Benefits = benefits;
                _editingOption.IsActive = ActiveSwitch.IsToggled;
                await _api.UpdateTripOptionAsync(_trip.Id, _editingOption);
            }

            ResetForm();
            await LoadTripAsync();
            FeedbackLabel.IsVisible = true;
            FeedbackLabel.TextColor = Color.FromArgb("#006C49");
            FeedbackLabel.Text = "Opción guardada.";
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

    private async Task ToggleAsync(TripOption opt)
    {
        if (_trip is null) return;
        var previous = opt.IsActive;
        opt.IsActive = !opt.IsActive;
        try
        {
            await _api.UpdateTripOptionAsync(_trip.Id, opt);
            await LoadTripAsync();
        }
        catch (Exception ex)
        {
            opt.IsActive = previous;
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async Task DeleteAsync(TripOption opt)
    {
        if (_trip is null) return;
        var ok = await DisplayAlertAsync("Eliminar", $"¿Eliminar la opción \"{opt.Name}\"?", "Sí", "No");
        if (!ok) return;

        try
        {
            await _api.DeleteTripOptionAsync(_trip.Id, opt.Id);
            if (_editingOption?.Id == opt.Id) ResetForm();
            await LoadTripAsync();
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
