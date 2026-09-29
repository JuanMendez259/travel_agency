using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.App.ViewModels;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

public partial class ClientHomePage : ContentPage
{
    private static readonly Color ChipSelectedBackground = Color.FromArgb("#512BD4");
    private static readonly Color ChipSelectedText = Colors.White;
    private static readonly Color ChipIdleBackground = Color.FromArgb("#E1E1E1");
    private static readonly Color ChipIdleText = Color.FromArgb("#141414");

    /// Numero de WhatsApp de la agencia en formato internacional, solo digitos
    /// con lada (52 = Mexico, 445 = Puebla). Vacio = se informa el Instagram.
    private const string AgencyWhatsApp = "5214444579256";

    private readonly ApiService _api;
    private List<TripListItem> _items = new();
    private string? _selectedCategory;

    public ClientHomePage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadTripsAsync();
    }

    private async Task LoadTripsAsync(bool showLoading = true)
    {
        if (showLoading)
        {
            Loading.IsRunning = true;
            Loading.IsVisible = true;
        }
        try
        {
            var trips = await _api.GetTripsAsync();

            var favIds = new HashSet<int>();
            try
            {
                var favorites = await _api.GetFavoritesAsync();
                favIds = favorites?.Select(t => t.Id).ToHashSet() ?? new HashSet<int>();
            }
            catch
            {
                // los favoritos no deben impedir ver la lista de viajes
            }

            var items = new List<TripListItem>();
            if (trips is not null)
            {
                foreach (var trip in trips)
                {
                    var thumb = await _api.GetTripImageAsync(trip.ImageUrl);
                    items.Add(new TripListItem(trip, thumb, favIds.Contains(trip.Id)));
                }
            }
            _items = items;
            BuildCategoryFilters();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar los viajes: {ex.Message}", "OK");
        }
        finally
        {
            if (showLoading)
            {
                Loading.IsRunning = false;
                Loading.IsVisible = false;
            }
        }
    }

    private void BuildCategoryFilters()
    {
        CategoryFiltersLayout.Children.Clear();

        var categories = _items
            .Select(i => i.Trip.Category?.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => c!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(CatalogOrder)
            .ThenBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (_selectedCategory is not null && !categories.Contains(_selectedCategory, StringComparer.CurrentCultureIgnoreCase))
            _selectedCategory = null;

        CategoryFiltersScroll.IsVisible = categories.Count > 0;
        if (categories.Count == 0) return;

        AddCategoryChip("Todas", null);
        foreach (var category in categories)
            AddCategoryChip(category, category);
    }

    private static int CatalogOrder(string category)
    {
        var index = Array.FindIndex(TripCategoryOptions.All, c => string.Equals(c, category, StringComparison.CurrentCultureIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    private void AddCategoryChip(string text, string? category)
    {
        var selected = category is null
            ? _selectedCategory is null
            : string.Equals(_selectedCategory, category, StringComparison.CurrentCultureIgnoreCase);

        var chip = new Button
        {
            Text = text,
            FontSize = 13,
            Padding = new Thickness(16, 0),
            HeightRequest = 34,
            CornerRadius = 17,
            MinimumWidthRequest = 0,
            BackgroundColor = selected ? ChipSelectedBackground : ChipIdleBackground,
            TextColor = selected ? ChipSelectedText : ChipIdleText
        };
        chip.Clicked += (_, _) => ToggleCategory(category);

        CategoryFiltersLayout.Children.Add(chip);
    }

    private void ToggleCategory(string? category)
    {
        _selectedCategory = _selectedCategory is not null
            && string.Equals(_selectedCategory, category, StringComparison.CurrentCultureIgnoreCase)
            ? null
            : category;

        BuildCategoryFilters();
        ApplyFilter();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var text = SearchEntry.Text?.Trim();
        var hasText = !string.IsNullOrEmpty(text);
        var hasCategory = !string.IsNullOrEmpty(_selectedCategory);

        IEnumerable<TripListItem> query = _items;

        if (hasCategory)
        {
            var category = _selectedCategory;
            query = query.Where(i => string.Equals(i.Trip.Category?.Trim(), category, StringComparison.CurrentCultureIgnoreCase));
        }

        if (hasText)
        {
            query = query.Where(i => Matches(i.Trip.Title, text)
                                   || Matches(i.Trip.Destination, text)
                                   || Matches(i.Trip.Category, text)
                                   || Matches(i.Trip.Description, text));
        }

        var matches = query.ToList();
        TripsList.ItemsSource = matches;
        TripsList.EmptyView = BuildEmptyView(hasText, hasCategory);
        UpdateCountLabel(matches.Count, _items.Count);
    }

    private string BuildEmptyView(bool hasText, bool hasCategory)
    {
        if (hasText && hasCategory)
            return $"Sin resultados para \"{SearchEntry.Text?.Trim()}\" en {_selectedCategory}.";
        if (hasText)
            return $"Sin resultados para \"{SearchEntry.Text?.Trim()}\".";
        if (hasCategory)
            return $"No hay viajes en {_selectedCategory}.";
        return "No hay viajes disponibles.";
    }

    private void UpdateCountLabel(int shown, int total)
    {
        var noun = total == 1 ? "viaje" : "viajes";
        TripsCountLabel.Text = shown == total
            ? $"{total} {noun}"
            : $"{shown} de {total} {noun}";
    }

    private static bool Matches(string? value, string text)
        => value is not null && value.Contains(text, StringComparison.CurrentCultureIgnoreCase);

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        try
        {
            await LoadTripsAsync(false);
        }
        finally
        {
            TripsRefresh.IsRefreshing = false;
        }
    }

    private async void OnBookClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Trip trip) return;
        await Shell.Current.GoToAsync($"trip?id={trip.Id}");
    }

    private async void OnFavoriteClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not TripListItem item) return;
        try
        {
            item.IsFavorite = item.IsFavorite
                ? await _api.RemoveFavoriteAsync(item.Trip.Id)
                : await _api.AddFavoriteAsync(item.Trip.Id);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnQuoteClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AgencyWhatsApp))
        {
            await DisplayAlertAsync("Cotizaciones",
                "Escríbenos por Instagram en @proxima_parada_tours para cotizar tu grupo privado.",
                "OK");
            return;
        }

        var message = Uri.EscapeDataString(
            "Hola, quiero cotizar una salida para un grupo privado de 10 o más viajeros.");
        var url = $"https://wa.me/{AgencyWhatsApp}?text={message}";

        try
        {
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception)
        {
            await DisplayAlertAsync("No se pudo abrir", url, "OK");
        }
    }

    private async void OnFavoritesClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("favorites");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de tu cuenta?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}