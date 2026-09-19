using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminStatsPage : ContentPage
{
    private readonly ApiService _api;
    private bool _loaded;

    public AdminStatsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        await LoadStats();
    }

    private async Task LoadStats()
    {
        try
        {
            var stats = await _api.GetAdminStatsAsync();
            if (stats is null) return;

            TripsKpi.Text = stats.TripsCount.ToString();
            SeatsKpi.Text = stats.SeatsSold.ToString();
            RevenueKpi.Text = $"${stats.RevenueTotal:N0}";
            OccupancyKpi.Text = $"{stats.OccupancyPercent:0.#}%";

            RevenueChart.Drawable = new RevenueChartDrawable(stats.MonthlyRevenue);

            BindableLayout.SetItemsSource(OccupancyList, stats.OccupancyByTrip);

            BindableLayout.SetItemsSource(DestinationsList, stats.TopDestinations);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudieron cargar las estadísticas: {ex.Message}", "OK");
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }
}

public class RevenueChartDrawable : IDrawable
{
    private static readonly Color BarColor = Color.FromArgb("#1976D2");
    private static readonly Color LabelColor = Color.FromArgb("#888888");
    private static readonly Color AxisColor = Color.FromArgb("#D0D0D0");

    private readonly List<MonthlyRevenue> _data;

    public RevenueChartDrawable(List<MonthlyRevenue> data)
    {
        _data = data ?? new List<MonthlyRevenue>();
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        if (_data.Count == 0)
        {
            canvas.FontColor = LabelColor;
            canvas.FontSize = 13;
            canvas.DrawString("Sin ingresos registrados", dirtyRect, HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        float topPadding = 8;
        float bottomPadding = 22;
        float chartHeight = dirtyRect.Height - topPadding - bottomPadding;
        float max = (float)(_data.Max(d => d.Amount));

        float slot = dirtyRect.Width / _data.Count;
        float barWidth = Math.Max(6, slot * 0.62f);

        canvas.StrokeColor = AxisColor;
        canvas.StrokeSize = 1;
        canvas.DrawLine(0, topPadding + chartHeight, dirtyRect.Width, topPadding + chartHeight);

        for (int i = 0; i < _data.Count; i++)
        {
            float x = slot * i + (slot - barWidth) / 2;
            float h = max > 0 ? chartHeight * ((float)_data[i].Amount / max) : 2;
            float y = topPadding + chartHeight - h;

            canvas.FillColor = BarColor;
            canvas.FillRoundedRectangle(x, y, barWidth, h, 3);

            canvas.FontColor = LabelColor;
            canvas.FontSize = 10;
            canvas.DrawString(MonthShort(_data[i].Month), x - 2, topPadding + chartHeight + 3, barWidth + 4, 18,
                HorizontalAlignment.Center, VerticalAlignment.Top);
        }
    }

    private static string MonthShort(string month)
    {
        var idx = month.LastIndexOf('-');
        return idx > 0 && month.Length - idx >= 3 ? month[(idx + 1)..] : month;
    }
}