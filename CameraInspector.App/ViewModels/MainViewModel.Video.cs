using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// Acciones auxiliares del reproductor de video.
/// </summary>
public sealed partial class MainViewModel
{
    [RelayCommand]
    private void TakeSnapshot()
    {
        if (SelectedDevice is null)
            return;

        // SaveFileDialog permite elegir el destino del PNG sin exponer rutas internas de almacenamiento.
        var dialog = new SaveFileDialog
        {
            Title = "Guardar captura de cámara",
            Filter = "Imagen PNG (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = $"Camera_{SelectedDevice.IpAddress.Replace('.', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            // snapshotSaved indica si LibVLC tenía un frame disponible y aceptó la solicitud.
            var snapshotSaved = _videoPlayerService.TakeSnapshot(dialog.FileName);
            StatusText = snapshotSaved
                ? $"Captura guardada: {dialog.FileName}"
                : "No hay un frame de video disponible para capturar.";
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo guardar la captura: {ex.Message}";
        }
    }
}
