using Microsoft.Maui.Controls;

namespace TravelAgency.App.Controls.Admin;

public partial class AdminPageHeader : ContentView
{
    public static readonly BindableProperty EyebrowProperty = BindableProperty.Create(
        nameof(Eyebrow), typeof(string), typeof(AdminPageHeader), string.Empty);

    public static readonly BindableProperty PageTitleProperty = BindableProperty.Create(
        nameof(PageTitle), typeof(string), typeof(AdminPageHeader), string.Empty);

    public static readonly BindableProperty ShowBackProperty = BindableProperty.Create(
        nameof(ShowBack), typeof(bool), typeof(AdminPageHeader), false);

    public static readonly BindableProperty ShowLogoutProperty = BindableProperty.Create(
        nameof(ShowLogout), typeof(bool), typeof(AdminPageHeader), true);

    public event EventHandler? BackRequested;
    public event EventHandler? LogoutRequested;

    public AdminPageHeader()
    {
        InitializeComponent();
    }

    public string Eyebrow
    {
        get => (string)GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public string PageTitle
    {
        get => (string)GetValue(PageTitleProperty);
        set => SetValue(PageTitleProperty, value);
    }

    public bool ShowBack
    {
        get => (bool)GetValue(ShowBackProperty);
        set => SetValue(ShowBackProperty, value);
    }

    public bool ShowLogout
    {
        get => (bool)GetValue(ShowLogoutProperty);
        set => SetValue(ShowLogoutProperty, value);
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        var handler = BackRequested;
        if (handler is not null)
        {
            handler.Invoke(this, EventArgs.Empty);
            return;
        }

        if (Shell.Current is null)
            return;

        if (Shell.Current.Navigation.NavigationStack.Count > 1)
            await Shell.Current.GoToAsync("..");
        else
            await Shell.Current.GoToAsync("//admintrips");
    }

    private void OnLogoutClicked(object? sender, EventArgs e)
    {
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }
}
