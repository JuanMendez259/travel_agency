using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminTripSeatsPage : ContentPage
{
    private static readonly Color AvailableColor = Color.FromArgb("#DAE2FD");
    private static readonly Color OccupiedColor = Color.FromArgb("#DBB92A");
    private static readonly Color CheckedInColor = Color.FromArgb("#006C49");
    private static readonly Color OnSurfaceColor = Color.FromArgb("#131B2E");
    private static readonly Color OnSurfaceVariantColor = Color.FromArgb("#404941");
    private static readonly Color OutlineColor = Color.FromArgb("#717970");

    private readonly ApiService _api;
    private List<TripSeatRow> _rows = new();
    private int _currentFloor = 1;
    private bool _twoFloors;
    private int _floor1Capacity;

    public string TripId { get; set; } = string.Empty;

    public AdminTripSeatsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadSeatsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar el mapa de asientos: {ex.Message}", "OK");
        }
    }

    private async Task LoadSeatsAsync(bool showLoading = true)
    {
        if (!int.TryParse(TripId, out var id)) return;

        if (showLoading)
        {
            Loading.IsRunning = true;
            Loading.IsVisible = true;
        }

        try
        {
            var map = await _api.GetTripSeatMapAsync(id);
            if (map is null)
            {
                await DisplayAlertAsync("Error", "No se encontró el viaje.", "OK");
                return;
            }

            RenderMap(map);
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

    private void RenderMap(TripSeatMap map)
    {
        _rows = map.Rows;
        _twoFloors = map.HasTwoFloors;
        _floor1Capacity = map.Floor1Capacity ?? 0;
        _currentFloor = 1;

        TripLabel.Text = map.TripTitle;
        FloorBadgeLabel.Text = map.HasTwoFloors ? "Dos pisos" : "Planta única";
        SummaryLabel.Text = $"{TransportTypeConverter.ToDisplay(map.TransportType)} · "
            + $"{map.Capacity} asientos · {map.OccupiedCount} ocupados · {map.AvailableCount} libres";
        FloorCapacityLabel.Text = map.HasTwoFloors
            ? $"Piso 1: {map.Floor1Capacity ?? 0} · Piso 2: {map.Floor2Capacity ?? 0}"
            : string.Empty;
        FloorCapacityLabel.IsVisible = map.HasTwoFloors;

        FloorSelector.IsVisible = _twoFloors;
        UpdateFloorButtons();

        OccupiedList.Clear();
        RenderMapRows();

        var occupied = map.Rows
            .SelectMany(r => r.Seats)
            .Where(s => !s.IsAisle && s.IsOccupied)
            .OrderBy(s => s.Number)
            .ToList();
        OccupiedCountLabel.Text = $"{occupied.Count} ocupados";

        if (occupied.Count == 0)
        {
            OccupiedList.Add(new Label
            {
                Text = "Todavía no hay asientos ocupados.",
                FontSize = 13,
                TextColor = Res("OnSurfaceVariant"),
                Margin = new Thickness(0, 2)
            });
            return;
        }

        foreach (var seat in occupied)
        {
            var who = seat.OccupiedBy ?? "Sin nombre registrado";
            var stateText = seat.CheckedIn
                ? "Check-in hecho"
                : seat.IsAssigned ? "Pasajero registrado · falta check-in" : "Reserva sin pasajero registrado";
            var detailColor = seat.CheckedIn ? Res("Secondary") : Res("OnWarningContainer");
            var badge = new Border
            {
                WidthRequest = 36,
                HeightRequest = 36,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                BackgroundColor = seat.CheckedIn ? Res("SecondaryContainer") : Res("WarningContainer"),
                Content = new Label
                {
                    Text = seat.CheckedIn ? "✓" : "•",
                    FontSize = seat.CheckedIn ? 17 : 20,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = detailColor,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };

            var details = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
            details.Add(new Label
            {
                Text = who,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Res("OnSurface")
            });
            details.Add(new Label
            {
                Text = _twoFloors
                    ? $"Piso {FloorOf(seat.Number)} · Asiento {seat.Number}"
                    : $"Asiento {seat.Number}",
                FontSize = 11,
                TextColor = Res("OnSurfaceVariant")
            });
            details.Add(new Label
            {
                Text = stateText,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = detailColor
            });

            var occupiedRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 10,
                Children = { badge, details }
            };
            Grid.SetColumn(details, 1);

            OccupiedList.Add(new Border
            {
                Stroke = Res("SurfaceContainerHigh"),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(10),
                BackgroundColor = Res("SurfaceContainerLow"),
                Content = occupiedRow
            });
        }
    }

    private View BuildSeat(TripSeat seat)
    {
        if (seat.IsAisle)
        {
            return new BoxView
            {
                WidthRequest = 18,
                HeightRequest = 48,
                Color = Colors.Transparent
            };
        }

        var background = !seat.IsOccupied
            ? AvailableColor
            : seat.CheckedIn ? CheckedInColor : OccupiedColor;

        var border = new Border
        {
            WidthRequest = 48,
            HeightRequest = 48,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
            Stroke = seat.CheckedIn ? CheckedInColor : Colors.Transparent,
            StrokeThickness = seat.CheckedIn ? 1 : 0,
            BackgroundColor = background,
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = seat.Number.ToString(),
                        TextColor = seat.IsOccupied && !seat.CheckedIn ? OnSurfaceColor : seat.IsOccupied ? Colors.White : OnSurfaceColor,
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = seat.CheckedIn ? "✓" : seat.IsOccupied ? "Ocupado" : "Libre",
                        TextColor = seat.IsOccupied && !seat.CheckedIn ? OnSurfaceColor : seat.IsOccupied ? Colors.White : OnSurfaceVariantColor,
                        FontSize = 8,
                        FontAttributes = seat.CheckedIn ? FontAttributes.Bold : FontAttributes.None,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };

        var status = seat.CheckedIn ? "check-in hecho" : seat.IsOccupied ? "ocupado" : "disponible";
        var whoDescription = seat.IsOccupied && !string.IsNullOrWhiteSpace(seat.OccupiedBy)
            ? $", {seat.OccupiedBy}, reserva {seat.BookingId}"
            : string.Empty;
        SemanticProperties.SetDescription(border, $"Asiento {seat.Number}, {status}{whoDescription}");
        SemanticProperties.SetHint(border, "Toca para ver el detalle del asiento");

        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                var status = seat.CheckedIn ? "Ocupado · check-in hecho" : seat.IsOccupied ? "Ocupado · falta check-in" : "Disponible";
                var who = seat.IsOccupied
                    ? $"\n{seat.OccupiedBy ?? "Titular sin pasajero registrado"} · Reserva #{seat.BookingId}"
                    : string.Empty;
                var title = _twoFloors
                    ? $"Piso {FloorOf(seat.Number)} · Asiento {seat.Number}"
                    : $"Asiento {seat.Number}";
                await DisplayAlertAsync(title, $"{status}{who}", "OK");
            })
        });

        return border;
    }

    private void RenderMapRows()
    {
        RowsContainer.Clear();
        foreach (var row in _rows.Where(r => r.Floor == _currentFloor))
        {
            var rowLayout = new Grid { ColumnSpacing = 4 };
            rowLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            rowLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(24, GridUnitType.Absolute)));
            rowLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var left = new HorizontalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center
            };
            var right = new HorizontalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center
            };
            var seatCount = 0;

            foreach (var seat in row.Seats)
            {
                if (seat.IsAisle) continue;
                (seatCount < 2 ? left : right).Add(BuildSeat(seat));
                seatCount++;
            }

            var rowLabel = new Label
            {
                Text = $"F{row.RowNumber}",
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#C0C9BE"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };
            rowLayout.Add(left, 0, 0);
            rowLayout.Add(rowLabel, 1, 0);
            rowLayout.Add(right, 2, 0);
            RowsContainer.Add(rowLayout);
        }
    }

    private int FloorOf(int number)
        => _floor1Capacity > 0 && number > _floor1Capacity ? 2 : 1;

    private void OnFloor1Clicked(object? sender, EventArgs e) => SelectFloor(1);

    private void OnFloor2Clicked(object? sender, EventArgs e) => SelectFloor(2);

    private void SelectFloor(int floor)
    {
        if (_currentFloor == floor) return;
        _currentFloor = floor;
        UpdateFloorButtons();
        RenderMapRows();
    }

    private void UpdateFloorButtons()
    {
        var activeBg = Res("Primary");
        var inactiveBg = Res("SurfaceContainerHighest");
        Floor1Button.BackgroundColor = _currentFloor == 1 ? activeBg : inactiveBg;
        Floor1Button.TextColor = _currentFloor == 1 ? Colors.White : OnSurfaceColor;
        Floor2Button.BackgroundColor = _currentFloor == 2 ? activeBg : inactiveBg;
        Floor2Button.TextColor = _currentFloor == 2 ? Colors.White : OnSurfaceColor;
    }

    private static Color Res(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
            return color;
        return Colors.Transparent;
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        try
        {
            await LoadSeatsAsync(false);
        }
        finally
        {
            SeatsRefresh.IsRefreshing = false;
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}
