using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CameraInspector.Core.Models;
using CameraInspector.Video;

namespace CameraInspector.App;

/// <summary>
/// Ventana dedicada a dispositivos de captura locales.
/// Mantiene aislada la experiencia USB/UVC de la grilla de cámaras IP.
/// </summary>
public partial class LocalCamerasWindow : Window
{
    private readonly LocalCameraService _cameraService;

    public LocalCamerasWindow(LocalCameraService cameraService)
    {
        ArgumentNullException.ThrowIfNull(cameraService);

        InitializeComponent();

        // _cameraService enumera y abre las fuentes locales de vídeo mediante DirectShow + LibVLC.
        _cameraService = cameraService;
        _cameraService.PlayerChanged += CameraService_PlayerChanged;

        Loaded += (_, _) => RefreshCameras();
        Closed += (_, _) =>
        {
            _cameraService.PlayerChanged -= CameraService_PlayerChanged;
            _cameraService.Stop();
        };
    }

    private void RefreshCameras()
    {
        try
        {
            // cameras combina DirectShow y, cuando es necesario, el respaldo PnP de Windows.
            var cameras = _cameraService.GetAvailableCameras();
            CameraList.ItemsSource = cameras;

            if (cameras.Count == 0)
            {
                // LastEnumerationDiagnostic conserva la causa técnica para diferenciar ausencia de cámara de un fallo del enumerador.
                StatusTextBlock.Text = _cameraService.LastEnumerationDiagnostic;
                return;
            }

            // previewCount indica cuántas fuentes pueden abrirse inmediatamente con LibVLC/DirectShow.
            var previewCount = cameras.Count(camera => camera.PreviewSupported);
            StatusTextBlock.Text = $"{cameras.Count} fuente(s) detectada(s) · {previewCount} con previsualización disponible.\n{_cameraService.LastEnumerationDiagnostic}";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"No se pudieron enumerar las cámaras locales: {ex.Message}";
        }
    }

    private void CameraList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // camera representa la fuente local seleccionada por el técnico.
        if (CameraList.SelectedItem is not LocalCameraDevice camera)
            return;

        SelectedCameraNameText.Text = camera.Name;

        if (!camera.PreviewSupported)
        {
            // Una entrada PnP puede existir sin una fuente DirectShow utilizable; no la tratamos como un error de enumeración.
            StatusTextBlock.Text = $"Detectada por {camera.DiscoverySource}, pero Windows no expone una fuente DirectShow utilizable para previsualización.";
            VideoSurface.MediaPlayer = null;
            return;
        }

        StatusTextBlock.Text = $"Abriendo: {camera.Name}...";

        try
        {
            // started indica si LibVLC pudo abrir la fuente DirectShow seleccionada.
            var started = _cameraService.Play(camera);
            if (!started)
            {
                StatusTextBlock.Text = "La cámara fue detectada, pero no se pudo abrir la previsualización. Verifique si otro programa está utilizando el dispositivo.";
                return;
            }

            StatusTextBlock.Text = $"Captura activa: {camera.Name}";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Error al abrir la cámara: {ex.Message}";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        // Stop libera el dispositivo de captura y deja disponible el driver para otras aplicaciones.
        _cameraService.Stop();
        VideoSurface.MediaPlayer = null;
        SelectedCameraNameText.Text = string.Empty;
        StatusTextBlock.Text = "Captura detenida.";
    }

    private void CameraService_PlayerChanged(object? sender, LibVLCSharp.Shared.MediaPlayer? player)
    {
        // LibVLC puede notificar desde otro hilo; la UI WPF debe actualizarse en Dispatcher.
        Dispatcher.InvokeAsync(() =>
        {
            VideoSurface.MediaPlayer = player;
        }, DispatcherPriority.Normal);
    }
}
