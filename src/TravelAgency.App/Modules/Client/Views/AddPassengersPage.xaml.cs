using Microsoft.Maui.Controls;

namespace TravelAgency.App.Modules.Client.Views;

public partial class AddPassengersPage : ContentPage
{
    private readonly List<Entry> _entries = new();
    public List<string>? Names { get; private set; }

    public AddPassengersPage(int count)
    {
        InitializeComponent();
        Title = count == 1 ? "Pasajero adicional" : "Pasajeros adicionales";

        for (var i = 0; i < count; i++)
        {
            var entry = new Entry
            {
                Placeholder = $"Nombre del pasajero {i + 1}",
                ReturnType = ReturnType.Done
            };
            _entries.Add(entry);
            EntriesLayout.Children.Add(entry);
        }
    }

    private void OnConfirmClicked(object? sender, EventArgs e)
    {
        var names = _entries.Select(x => x.Text?.Trim() ?? "").ToList();
        if (names.Any(string.IsNullOrEmpty))
        {
            DisplayAlert("Datos incompletos", "Completa el nombre de todos los pasajeros.", "OK");
            return;
        }

        Names = names;
        OnCancelClicked(sender, e);
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}