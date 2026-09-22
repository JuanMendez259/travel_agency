using TravelAgency.App.Converters;
using TravelAgency.App.Services;
using TravelAgency.Shared.Models;

namespace TravelAgency.App.Modules.Admin.Views;

public partial class AdminDashboardPage : ContentPage, IQueryAttributable
{
    private static readonly string[] RoleNames = { "Administrador", "Coordinador", "Cliente" };
    private static readonly UserRole[] RoleValues = { UserRole.Admin, UserRole.Coordinador, UserRole.Client };

    private readonly ApiService _api;
    private readonly SessionService _session;
    private FileResult? _selectedImage;
    private Trip? _editingTrip;

    public AdminDashboardPage(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        TransportTypePicker.ItemsSource = TransportTypeConverter.Options.ToList();
        TransportTypePicker.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadUsersAsync();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("editId", out var value) && int.TryParse(value?.ToString(), out var id))
        {
            _ = LoadForEditAsync(id);
        }
    }

    private async Task LoadForEditAsync(int id)
    {
        try
        {
            var trips = await _api.GetAdminTripsAsync();
            var trip = trips?.FirstOrDefault(t => t.Id == id);
            if (trip is null) return;

            StartEdit(trip);
            await LoadPreviewAsync(trip.ImageUrl);
            await PageScroll.ScrollToAsync(0, 0, true);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            var trip = new Trip
            {
                Title = TitleEntry.Text?.Trim(),
                Destination = DestinationEntry.Text?.Trim(),
                Description = DescriptionEditor.Text?.Trim(),
                StartDate = StartDatePicker.Date.GetValueOrDefault(),
                EndDate = EndDatePicker.Date.GetValueOrDefault(),
                Price = decimal.TryParse(PriceEntry.Text, out var price) ? price : 0,
                ChildPrice = decimal.TryParse(ChildPriceEntry.Text, out var childPrice) ? childPrice : (decimal?)null,
                Capacity = int.TryParse(CapacityEntry.Text, out var capacity) ? capacity : 0,
                TransportType = (TransportType)Math.Max(0, TransportTypePicker.SelectedIndex),
                IsActive = true,
            };

            if (string.IsNullOrEmpty(trip.Title) || string.IsNullOrEmpty(trip.Destination))
            {
                await DisplayAlertAsync("Error", "Título y destino son obligatorios.", "OK");
                return;
            }

            if (trip.EndDate < trip.StartDate)
            {
                await DisplayAlertAsync("Error", "La fecha de fin no puede ser anterior al inicio.", "OK");
                return;
            }

            if (trip.Capacity < 1)
            {
                await DisplayAlertAsync("Error", "La capacidad debe ser al menos 1 asiento.", "OK");
                return;
            }

            if (_editingTrip is not null)
            {
                var updated = await _api.UpdateTripAsync(_editingTrip.Id, trip);
                if (_selectedImage is not null && updated is not null)
                {
                    updated = await _api.UploadTripImageAsync(updated.Id, _selectedImage);
                }
            }
            else
            {
                var created = await _api.CreateTripAsync(trip);
                if (_selectedImage is not null && created is not null)
                {
                    var updated = await _api.UploadTripImageAsync(created.Id, _selectedImage);
                    if (updated?.ImageUrl is not null)
                    {
                        created.ImageUrl = updated.ImageUrl;
                    }
                }
            }

            var wasEditing = _editingTrip is not null;
            ResetFormToCreateMode();
            ClearForm();
            await DisplayAlertAsync("Listo", wasEditing ? "Cambios guardados." : "Viaje publicado.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async Task LoadPreviewAsync(string? imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl))
        {
            ImagePreview.IsVisible = false;
            ImageNameLabel.IsVisible = false;
            return;
        }

        ImagePreview.Source = await _api.GetTripImageAsync(imageUrl);
        if (ImagePreview.Source is not null)
        {
            ImagePreview.IsVisible = true;
            ImageNameLabel.Text = _selectedImage is null ? "Imagen actual" : _selectedImage.FileName;
            ImageNameLabel.IsVisible = true;
        }
    }

    private void OnCancelEditClicked(object? sender, EventArgs e)
    {
        ResetFormToCreateMode();
        ClearForm();
    }

    private void StartEdit(Trip trip)
    {
        _editingTrip = trip;
        _selectedImage = null;
        TitleEntry.Text = trip.Title;
        DestinationEntry.Text = trip.Destination;
        DescriptionEditor.Text = trip.Description;
        StartDatePicker.Date = trip.StartDate;
        EndDatePicker.Date = trip.EndDate;
        PriceEntry.Text = trip.Price.ToString();
        ChildPriceEntry.Text = trip.ChildPrice?.ToString();
        CapacityEntry.Text = trip.Capacity.ToString();
        TransportTypePicker.SelectedIndex = (int)trip.TransportType;

        ImagePreview.Source = null;
        ImagePreview.IsVisible = false;
        ImageNameLabel.IsVisible = false;

        FormTitleLabel.Text = "Editar viaje";
        SaveButton.Text = "Guardar cambios";
        CancelEditButton.IsVisible = true;
    }

    private void ResetFormToCreateMode()
    {
        _editingTrip = null;
        _selectedImage = null;
        FormTitleLabel.Text = "Nuevo viaje";
        SaveButton.Text = "Guardar viaje";
        CancelEditButton.IsVisible = false;
    }

    private void ClearForm()
    {
        TitleEntry.Text = string.Empty;
        DestinationEntry.Text = string.Empty;
        PriceEntry.Text = string.Empty;
        ChildPriceEntry.Text = string.Empty;
        CapacityEntry.Text = string.Empty;
        DescriptionEditor.Text = string.Empty;
        TransportTypePicker.SelectedIndex = 0;
        _selectedImage = null;
        ImagePreview.Source = null;
        ImagePreview.IsVisible = false;
        ImageNameLabel.Text = string.Empty;
        ImageNameLabel.IsVisible = false;
    }

    private async void OnPickImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var results = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
            {
                Title = "Selecciona una imagen del viaje"
            });

            var result = results?.FirstOrDefault();
            if (result is null) return;

            using var stream = await result.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);

            ImagePreview.Source = ImageSource.FromStream(() => new MemoryStream(memory.ToArray()));
            ImagePreview.IsVisible = true;
            ImageNameLabel.Text = result.FileName;
            ImageNameLabel.IsVisible = true;

            _selectedImage = result;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Cerrar sesión", "¿Deseas salir de la cuenta de administrador?", "Sí", "No");
        if (confirm) App.GoToLogin();
    }

    private async Task LoadUsersAsync()
    {
        UsersErrorLabel.IsVisible = false;
        try
        {
            var users = await _api.GetUsersAsync();
            RenderUsers(users?.OrderBy(u => u.Name).ToList() ?? new List<User>());
        }
        catch (Exception ex)
        {
            UsersLayout.Children.Clear();
            UsersErrorLabel.Text = $"No se pudieron cargar los usuarios: {ex.Message}";
            UsersErrorLabel.IsVisible = true;
        }
    }

    private void RenderUsers(List<User> users)
    {
        UsersLayout.Children.Clear();

        if (users.Count == 0)
        {
            UsersLayout.Children.Add(new Label
            {
                Text = "No hay usuarios registrados.",
                TextColor = Colors.Gray,
                FontSize = 14
            });
            return;
        }

        foreach (var user in users)
        {
            UsersLayout.Children.Add(BuildUserRow(user));
        }
    }

    private View BuildUserRow(User user)
    {
        var isSelf = _session.UserId == user.Id;
        var roleIndex = Array.IndexOf(RoleValues, user.Role);
        if (roleIndex < 0) roleIndex = 0;

        var picker = new Picker
        {
            ItemsSource = RoleNames,
            SelectedIndex = roleIndex,
            IsEnabled = !isSelf,
            WidthRequest = 150
        };

        var save = new Button
        {
            Text = "Guardar",
            IsEnabled = !isSelf,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalOptions = LayoutOptions.Center
        };
        save.Clicked += async (_, _) =>
        {
            if (picker.SelectedIndex < 0 || picker.SelectedIndex == roleIndex) return;

            var role = RoleValues[picker.SelectedIndex];
            save.IsEnabled = false;
            try
            {
                await _api.UpdateUserRoleAsync(user.Id, role);
                user.Role = role;
                await DisplayAlertAsync("Listo", $"El rol de {user.Name} ahora es {RoleNames[picker.SelectedIndex]}.", "OK");
                roleIndex = picker.SelectedIndex;
            }
            catch (Exception ex)
            {
                picker.SelectedIndex = roleIndex;
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
            finally
            {
                save.IsEnabled = !isSelf;
            }
        };

        var info = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        info.Children.Add(new Label
        {
            Text = isSelf ? $"{user.Name} (tú)" : user.Name,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold
        });
        info.Children.Add(new Label
        {
            Text = user.Email,
            FontSize = 12,
            TextColor = Colors.Gray
        });

        var actions = new HorizontalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children = { picker, save }
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Padding = new Thickness(12)
        };
        grid.Add(info, 0);
        grid.Add(actions, 1);

        return new Border
        {
            StrokeThickness = 0,
            BackgroundColor = new Color(0.92f, 0.93f, 0.95f),
            Content = grid
        };
    }
}