using Microsoft.Maui.Controls;
using TravelAgency.App.Services;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(BookingId), "bookingId")]
[QueryProperty(nameof(Count), "count")]
public partial class AddPassengersPage : ContentPage
{
    private readonly ApiService _api;
    private readonly List<Entry> _entries = new();
    private int _bookingId;
    private int _count;
    private bool _built;

    public string BookingId { get; set; } = string.Empty;
    public string Count { get; set; } = string.Empty;

    public AddPassengersPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_built) return;

        _bookingId = int.TryParse(BookingId, out var bid) ? bid : 0;
        _count = int.TryParse(Count, out var c) ? c : 0;

        Title = _count == 1 ? "Pasajero adicional" : "Pasajeros adicionales";
        ConfirmButton.IsEnabled = _bookingId > 0 && _count > 0;

        if (_bookingId <= 0 || _count <= 0)
        {
            EntriesLayout.Children.Add(new Label
            {
                Text = "No hay asientos adicionales que registrar en este momento.",
                FontSize = 14,
                TextColor = Colors.Gray
            });
            _built = true;
            return;
        }

        for (var i = 0; i < _count; i++)
        {
            var entry = new Entry
            {
                Placeholder = $"Nombre del pasajero {i + 1}",
                ReturnType = ReturnType.Done
            };
            _entries.Add(entry);
            EntriesLayout.Children.Add(entry);
        }

        _built = true;
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (_bookingId <= 0 || _count <= 0) return;

        var names = _entries.Select(x => x.Text?.Trim() ?? "").ToList();
        if (names.Any(string.IsNullOrEmpty))
        {
            await DisplayAlertAsync("Datos incompletos", "Completa el nombre de todos los pasajeros.", "OK");
            return;
        }

        ConfirmButton.IsEnabled = false;
        try
        {
            await _api.CreatePassengersAsync(_bookingId, names.ToArray());
            await DisplayAlertAsync("Listo", "Acompañantes registrados. Ya puedes ver sus códigos QR.", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            ConfirmButton.IsEnabled = true;
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}