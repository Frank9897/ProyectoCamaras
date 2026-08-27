using Microsoft.Win32;
using System.Text;
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
    private LocalCameraDevice? _selectedCamera;

    public LocalCamerasWindow(LocalCameraService cameraService)
    {
        ArgumentNullException.ThrowIfNull(cameraService);

        InitializeComponent();

        // _cameraService enumera, abre, captura snapshots y controla grabaciones locales.
        _cameraService = cameraService;
        _cameraService.PlayerChanged += CameraService_PlayerChanged;

        Loaded += (_, _) => RefreshCameras();
        Closed += (_, _) =>
        {
            _cameraService.PlayerChanged -= CameraService_PlayerChanged;
            _cameraService.StopRecording();
            _cameraService.Stop();
        };
    }

    private void RefreshCameras()
    {
        try
        {
            // cameras combina DirectShow y el respaldo PnP de Windows.
            var cameras = _cameraService.GetAvailableCameras();
            CameraList.ItemsSource = cameras;

            if (cameras.Count == 0)
            {
                StatusTextBlock.Text = _cameraService.LastEnumerationDiagnostic;
                SelectedCameraNameText.Text = string.Empty;
                UpdateActionButtons(null);
                return;
            }

            // previewCount muestra cuántas fuentes pueden abrirse con el pipeline multimedia actual.
            var previewCount = cameras.Count(camera => camera.PreviewSupported);
            StatusTextBlock.Text =
                $"{cameras.Count} fuente(s) detectada(s) · {previewCount} con previsualización disponible.\n" +
                _cameraService.LastEnumerationDiagnostic;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"No se pudieron enumerar las cámaras locales: {ex.Message}";
            UpdateActionButtons(null);
        }
    }

    private async void CameraList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // camera representa la fuente local seleccionada por el técnico.
        if (CameraList.SelectedItem is not LocalCameraDevice camera)
        {
            _selectedCamera = null;
            UpdateActionButtons(null);
            return;
        }

        _selectedCamera = camera;
        SelectedCameraNameText.Text = camera.Name;
        UpdateActionButtons(camera);

        StatusTextBlock.Text =
            $"Origen: {camera.DiscoverySource} · Transporte: {camera.Transport} · " +
            $"VID: {camera.UsbVendorId ?? "N/D"} · PID: {camera.UsbProductId ?? "N/D"}\n" +
            $"Estado: {camera.Status} · Preview declarado: {(camera.PreviewSupported ? "Sí" : "No")}";

        if (!camera.PreviewSupported)
        {
            // Una entrada PnP puede existir sin una fuente DirectShow utilizable.
            return;
        }

        try
        {
            StatusTextBlock.Text += "\nNegociando resolución y salida de vídeo...";

            // started solo será true cuando LibVLC haya creado una salida de vídeo real.
            var started = await _cameraService.PlayAsync(camera);
            if (!started)
            {
                StatusTextBlock.Text += "\nNo se consiguió una salida de vídeo. Verifique permisos, driver o si otra aplicación usa la cámara.";
                UpdateActionButtons(camera);
                return;
            }

            // outputCount permite diferenciar una reproducción real de un Play() aceptado sin frames.
            var outputCount = _cameraService.VideoOutputCount;
            StatusTextBlock.Text += $"\nPREVIEW FUNCIONANDO · salidas de vídeo: {outputCount}";
            UpdateActionButtons(camera);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text += "\nApertura cancelada.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text += $"\nError al abrir la cámara: {ex.Message}";
            UpdateActionButtons(camera);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        // Stop evita dejar el dispositivo físico ocupado mientras se vuelve a enumerar.
        _cameraService.Stop();
        RefreshCameras();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _cameraService.Stop();
        VideoSurface.MediaPlayer = null;
        StatusTextBlock.Text = "Previsualización detenida.";
        UpdateActionButtons(_selectedCamera);
    }

    private void SnapshotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCamera is null || _cameraService.VideoOutputCount == 0)
            return;

        // dialog permite elegir el nombre y ubicación del PNG sin escribir rutas manualmente.
        var dialog = new SaveFileDialog
        {
            Title = "Guardar snapshot de cámara local",
            Filter = "Imagen PNG (*.png)|*.png",
            FileName = $"CameraInspector_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            // saved indica si LibVLC pudo capturar desde la salida de vídeo activa.
            var saved = _cameraService.TakeSnapshot(dialog.FileName);
            StatusTextBlock.Text = saved
                ? $"Snapshot guardado: {dialog.FileName}"
                : "No se pudo capturar el snapshot. La salida de vídeo no está disponible.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Error al capturar snapshot: {ex.Message}";
        }
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCamera is null || _cameraService.VideoOutputCount == 0)
            return;

        // dialog permite elegir el archivo MP4 de salida.
        var dialog = new SaveFileDialog
        {
            Title = "Guardar grabación de cámara local",
            Filter = "Video MP4 (*.mp4)|*.mp4",
            FileName = $"CameraInspector_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            // started indica si se pudo crear el segundo pipeline de captura para grabar sin apagar la preview.
            var started = _cameraService.StartRecording(dialog.FileName);
            StatusTextBlock.Text = started
                ? $"● GRABANDO · {dialog.FileName}"
                : "No se pudo iniciar la grabación. La cámara debe estar previsualizándose.";
            UpdateActionButtons(_selectedCamera);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Error al iniciar grabación: {ex.Message}";
        }
    }

    private void StopRecordButton_Click(object sender, RoutedEventArgs e)
    {
        _cameraService.StopRecording();
        StatusTextBlock.Text = "Grabación detenida.";
        UpdateActionButtons(_selectedCamera);
    }

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCamera is null)
            return;

        // info construye una ficha técnica sin incluir secretos.
        var info = new StringBuilder()
            .AppendLine($"Nombre: {_selectedCamera.Name}")
            .AppendLine($"Origen: {_selectedCamera.DiscoverySource}")
            .AppendLine($"Transporte: {_selectedCamera.Transport}")
            .AppendLine($"Estado: {_selectedCamera.Status}")
            .AppendLine($"VID: {_selectedCamera.UsbVendorId ?? "N/D"}")
            .AppendLine($"PID: {_selectedCamera.UsbProductId ?? "N/D"}")
            .AppendLine($"Preview: {_selectedCamera.PreviewSupported}")
            .AppendLine($"DevicePath: {_selectedCamera.DevicePath ?? "N/D"}")
            .AppendLine($"Moniker: {_selectedCamera.MonikerString ?? "N/D"}")
            .ToString();

        MessageBox.Show(
            this,
            info,
            "Camera Inspector — Información de cámara local",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void UpdateActionButtons(LocalCameraDevice? camera)
    {
        // hasPreview indica que LibVLC creó una salida de vídeo real.
        var hasPreview = camera is not null && _cameraService.VideoOutputCount > 0;
        SnapshotButton.IsEnabled = hasPreview;
        RecordButton.IsEnabled = hasPreview && !_cameraService.IsRecording;
        StopRecordButton.IsEnabled = _cameraService.IsRecording;
        StopButton.IsEnabled = hasPreview;
        InfoButton.IsEnabled = camera is not null;
    }

    private void CameraService_PlayerChanged(object? sender, LibVLCSharp.Shared.MediaPlayer? player)
    {
        // LibVLC puede notificar desde otro hilo; WPF debe actualizarse mediante Dispatcher.
        Dispatcher.InvokeAsync(() =>
        {
            VideoSurface.MediaPlayer = player;
            UpdateActionButtons(_selectedCamera);
        }, DispatcherPriority.Normal);
    }
}
