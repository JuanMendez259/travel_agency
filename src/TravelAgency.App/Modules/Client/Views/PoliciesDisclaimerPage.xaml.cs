namespace TravelAgency.App.Modules.Client.Views;

public partial class PoliciesDisclaimerPage : ContentPage
{
    private const string ConfirmPhrase = "ACEPTO";

    private Action<bool>? _onFinished;

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
    }

    private void OnPhraseTextChanged(object? sender, TextChangedEventArgs e)
    {
        FinalConfirmButton.IsEnabled = string.Equals(PhraseEntry.Text?.Trim(), ConfirmPhrase, StringComparison.OrdinalIgnoreCase);
    }

    private async void OnFinalConfirmClicked(object? sender, EventArgs e)
    {
        var finished = _onFinished;
        _onFinished = null;
        await this.Navigation.PopModalAsync();
        finished?.Invoke(true);
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        var finished = _onFinished;
        _onFinished = null;
        await this.Navigation.PopModalAsync();
        finished?.Invoke(false);
    }
}