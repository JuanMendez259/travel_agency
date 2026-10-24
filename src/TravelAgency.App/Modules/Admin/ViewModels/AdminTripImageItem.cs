using System.ComponentModel;

namespace TravelAgency.App.Modules.Admin.ViewModels;

/// <summary>
/// Imagen de la galeria de un viaje en el panel de administracion. Puede ser
/// una imagen ya guardada, una seleccion local pendiente de subir o la portada
/// heredada de viajes que aun no tienen galeria.
/// </summary>
public sealed class AdminTripImageItem : INotifyPropertyChanged
{
    private bool _isCover;
    private bool _isUploaded;

    public int? Id { get; set; }
    public string? Url { get; set; }
    public FileResult? File { get; init; }
    public byte[]? PreviewBytes { get; init; }
    public ImageSource Source { get; init; } = null!;

    public bool IsNew => File is not null && !_isUploaded;
    public bool IsVisualOnly => File is null && !Id.HasValue;
    public bool IsEditable => !IsVisualOnly;
    public bool IsDeleted { get; set; }
    public int Order { get; set; }

    public bool IsCover
    {
        get => _isCover;
        set
        {
            if (_isCover == value) return;
            _isCover = value;
            NotifyCoverChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Marca la imagen como subida. Evita volver a enviarla si el guardado
    /// falla despues de que el servidor ya la creo.
    /// </summary>
    public void MarkUploaded(int id, string? url)
    {
        Id = id;
        if (!string.IsNullOrWhiteSpace(url)) Url = url;
        _isUploaded = true;
    }

    public void NotifyCoverChanged()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCover)));
}
