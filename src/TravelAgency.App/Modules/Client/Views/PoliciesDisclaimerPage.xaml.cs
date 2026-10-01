namespace TravelAgency.App.Modules.Client.Views;

public partial class PoliciesDisclaimerPage : ContentPage
{
    const double ThumbSize = 48;
    const double ThumbMargin = 4;
    const double CompleteRatio = 0.85;
    const uint SwipeDuration = 200;

    Action<bool>? _onFinished;
    bool _swipeCompleted;

    public PoliciesDisclaimerPage()
    {
        InitializeComponent();
    }

    public static async Task<bool> PresentAsync(INavigation navigation)
    {
        var page = new PoliciesDisclaimerPage();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        page._onFinished = result => tcs.TrySetResult(result);
        await navigation.PushModalAsync(page);
        return await tcs.Task;
    }

    private void OnPoliciesCheckedChanged(object? sender, CheckedChangedEventArgs e)
    {
        StepTwoPanel.IsVisible = e.Value;
        if (!e.Value)
            ResetSwipe();
    }

    double MaxTravel()
    {
        var trackWidth = SwipeTrack.Width;
        return Math.Max(0, trackWidth - ThumbSize - (ThumbMargin * 2));
    }

    async void OnSwipePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_swipeCompleted) return;

        var max = MaxTravel();
        if (max <= 0) return;

        if (e.StatusType == GestureStatus.Completed)
        {
            if (SwipeThumb.TranslationX >= max * CompleteRatio)
                await CompleteSwipeAsync();
            else
                await ResetSwipeAsync();
            return;
        }

        if (e.StatusType is GestureStatus.Started or GestureStatus.Canceled)
        {
            if (e.StatusType == GestureStatus.Canceled)
                await ResetSwipeAsync();
            return;
        }

        var travel = Math.Clamp(e.TotalX, 0, max);
        SwipeThumb.TranslationX = travel;
        SwipeFill.WidthRequest = travel + ThumbSize + ThumbMargin;
        SwipeLabel.Opacity = 1 - (travel / max);
    }

    async Task CompleteSwipeAsync()
    {
        var max = MaxTravel();

        await SwipeThumb.TranslateTo(max, 0, SwipeDuration, Easing.CubicOut);
        SwipeFill.WidthRequest = SwipeTrack.Width;

        _swipeCompleted = true;
        SwipeLabel.Text = "Condiciones aceptadas ✓";
        SwipeLabel.Opacity = 1;
        SwipeLabel.TextColor = Color.FromArgb("#FFFFFF");
        FinalConfirmButton.IsEnabled = true;
    }

    async Task ResetSwipeAsync()
    {
        await SwipeThumb.TranslateTo(0, 0, SwipeDuration, Easing.CubicOut);
        ResetSwipe();
    }

    void ResetSwipe()
    {
        _swipeCompleted = false;
        SwipeThumb.TranslationX = 0;
        SwipeFill.WidthRequest = 0;
        SwipeLabel.Text = "Acepto las condiciones";
        SwipeLabel.Opacity = 1;
        SwipeLabel.TextColor = Color.FromArgb("#404941");
        FinalConfirmButton.IsEnabled = false;
    }

    async void OnFinalConfirmClicked(object? sender, EventArgs e)
    {
        var finished = _onFinished;
        _onFinished = null;
        await this.Navigation.PopModalAsync();
        finished?.Invoke(true);
    }

    async void OnCloseClicked(object? sender, EventArgs e)
    {
        var finished = _onFinished;
        _onFinished = null;
        await this.Navigation.PopModalAsync();
        finished?.Invoke(false);
    }
}
