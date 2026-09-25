using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

[QueryProperty(nameof(TripId), "id")]
public partial class AdminTripSeatsPage : ContentPage
{
    private static readonly Color AvailableColor = Color.FromArgb("#D9D9D9");
    private static readonly Color OccupiedColor = Color.FromArgb("#DBB92A");
    private static readonly Color CheckedInColor = Color.FromArgb("#2F855A");

    private readonly ApiService _api;

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
        TripLabel.Text = map.TripTitle;
        SummaryLabel.Text = $"{TransportTypeConverter.ToDisplay(map.TransportType)} · "
            + $"{map.Capacity} asientos · {map.OccupiedCount} ocupados · {map.AvailableCount} libres";

        RowsContainer.Clear();
        OccupiedList.Clear();

        foreach (var row in map.Rows)
        {
            var rowLayout = new HorizontalStackLayout
            {
                Spacing = 10,
                HorizontalOptions = LayoutOptions.Center
            };

            foreach (var seat in row.Seats)
            {
                rowLayout.Add(BuildSeat(seat));
            }

            RowsContainer.Add(rowLayout);
        }

        var occupied = map.Rows
            .SelectMany(r => r.Seats)
            .Where(s => !s.IsAisle && s.IsOccupied)
            .OrderBy(s => s.Number)
            .ToList();

        if (occupied.Count == 0)
        {
            OccupiedList.Add(new Label
            {
                Text = "Todavía no hay asientos ocupados.",
                FontSize = 13,
                TextColor = Colors.Gray
            });
            return;
        }

        foreach (var seat in occupied)
        {
            var who = seat.CheckedIn
                ? $"{seat.OccupiedBy} (check-in hecho)"
                : seat.OccupiedBy;

            OccupiedList.Add(new Border
            {
                StrokeThickness = 0,
                Padding = new Thickness(12, 8),
                BackgroundColor = Color.FromArgb("#EEEEEE"),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label
                        {
                            Text = $"Asiento {seat.Number}",
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold
                        },
                        new Label { Text = who, FontSize = 12 },
                        new Label
                        {
                            Text = seat.IsAssigned
                                ? $"Reserva #{seat.BookingId} · pasajero registrado"
                                : $"Reserva #{seat.BookingId} · titular sin pasajero registrado",
                            FontSize = 11,
                            TextColor = Colors.Gray
                        }
                    }
                }
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
                HeightRequest = 44,
                Color = Colors.Transparent
            };
        }

        var background = !seat.IsOccupied
            ? AvailableColor
            : seat.CheckedIn ? CheckedInColor : OccupiedColor;

        var border = new Border
        {
            WidthRequest = 44,
            HeightRequest = 44,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
            Stroke = Colors.Transparent,
            BackgroundColor = background,
            Content = new Label
            {
                Text = seat.Number.ToString(),
                TextColor = seat.IsOccupied ? Colors.White : Colors.Black,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                var status = seat.CheckedIn ? "Ocupado · check-in hecho" : seat.IsOccupied ? "Ocupado" : "Disponible";
                var who = seat.IsOccupied ? $"\n{seat.OccupiedBy} · Reserva #{seat.BookingId}" : string.Empty;
                await DisplayAlertAsync($"Asiento {seat.Number}", $"{status}{who}", "OK");
            })
        });

        return border;
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
