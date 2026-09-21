using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Client.Views;

[QueryProperty(nameof(TripId), "tripId")]
public partial class ClientRateTripPage : ContentPage
{
    private static readonly Color StarActiveColor = Color.FromArgb("#F6AD55");
    private static readonly Color StarInactiveColor = Color.FromArgb("#2D3748");

    private readonly ApiService _api;
    private int _selectedRating;

    public string TripId { get; set; } = string.Empty;

    public ClientRateTripPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!int.TryParse(TripId, out var id)) return;

        try
        {
            var trips = await _api.GetTripsAsync();
            var trip = trips?.FirstOrDefault(t => t.Id == id);
            if (trip is null)
            {
                await DisplayAlertAsync("Error", "No se encontró el viaje.", "OK");
                return;
            }

            TitleLabel.Text = trip.Title;
            DestinationLabel.Text = trip.Destination;

            var my = await _api.GetMyTripRatingAsync(id);
            if (my is not null)
            {
                _selectedRating = my.Rating;
                CommentEditor.Text = my.Comment;
                SaveButton.Text = "Actualizar calificación";
                RenderStars();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo cargar el viaje: {ex.Message}", "OK");
        }
    }

    private void OnStarClicked(object? sender, EventArgs e)
    {
        var index = StarsLayout.Children
            .TakeWhile(c => !ReferenceEquals(c, sender))
            .Count();
        _selectedRating = Math.Min(index + 1, 5);
        RenderStars();
    }

    private void RenderStars()
    {
        for (var i = 0; i < StarsLayout.Children.Count; i++)
        {
            if (StarsLayout.Children[i] is not Button button) continue;
            var active = i < _selectedRating;
            button.BackgroundColor = active ? StarActiveColor : StarInactiveColor;
            button.TextColor = active ? Colors.White : Colors.White;
        }

        RatingLabel.Text = _selectedRating switch
        {
            0 => "Selecciona de 1 a 5 estrellas",
            _ => $"{new string('★', _selectedRating)}{new string('☆', 5 - _selectedRating)} ({_selectedRating}/5)"
        };
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (!int.TryParse(TripId, out var id)) return;

        if (_selectedRating < 1)
        {
            await DisplayAlertAsync("Calificación", "Selecciona de 1 a 5 estrellas.", "OK");
            return;
        }

        try
        {
            var comment = string.IsNullOrWhiteSpace(CommentEditor.Text) ? null : CommentEditor.Text.Trim();
            var saved = await _api.SubmitTripRatingAsync(id, _selectedRating, comment);
            if (saved is null) return;

            await DisplayAlertAsync("¡Gracias!", "Tu calificación se guardó correctamente.", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}