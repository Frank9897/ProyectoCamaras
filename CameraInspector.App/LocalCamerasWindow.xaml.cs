using Microsoft.Win32;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CameraInspector.Core.Models;
using CameraInspector.Video;

namespace CameraInspector.App;

/// <summary>
/// Ventana dedicada a dispositivos de captura locales.
/// Recibe frames de la capa Video y no depende de LibVLC para renderizar cámaras UVC.
/// </summary>
public partial class LocalCamerasWindow : Window
{
    private readonly LocalCameraService _cameraService;
    private LocalCameraDevice? _selectedCamera;

    public LocalCamerasWindow(LocalCameraService cameraService)
    {
        ArgumentNullException.ThrowIfNull(cameraService);

        InitializeComponent();

        // _cameraService enumera, captura frames, genera snapshots y controla grabaciones locales.
        _cameraService = cameraService;
        _cameraService.FrameReady += CameraService_FrameReady;

        Loaded += (_, _) => RefreshCameras();
        Closed += (_, _) =>
        {
            _cameraService.FrameReady -= CameraService_FrameReady;
            _cameraService.Stop();
        };

        UpdateActionButtons(null);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshCameras();

    private void RefreshCameras()
    {
        try
        {
            // cameras contiene las fuentes locales que Windows expone mediante DirectShow.
            var cameras = _cameraService.GetAvailableCameras();
            CameraList.ItemsSource = cameras;

            if (cameras.Count == 0)
            {
                _selectedCamera = null;
                SelectedCameraNameText.Text = string.Empty;
                CaptureStateTextBlock.Text = "SIN DISPOSITIVOS";
                ClearPreview();
                StatusTextBlock.Text = _cameraService.LastEnumerationDiagnostic;
                UpdateActionButtons(null);
                return;
            }

            // Mantenemos la selección actual si el dispositivo sigue conectado; de lo contrario usamos el primero.
            var previousName = _selectedCamera?.Name;
            var selected = cameras.FirstOrDefault(item => string.Equals(item.Name, previousName, StringComparison.OrdinalIgnoreCase))
                           ?? cameras[0];
            CameraList.SelectedItem = selected;

            StatusTextBlock.Text =
                $"{cameras.Count} cámara(s) local(es) detectada(s).\n{_cameraService.LastEnumerationDiagnostic}";
        }
        catch (Exception ex)
        {
            _selectedCamera = null;
            ClearPreview();
            StatusTextBlock.Text = $"Error enumerando cámaras locales: {ex.Message}";
            UpdateActionButtons(null);
        }
    }

    private async void CameraList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // camera representa la cámara local elegida por el técnico.
        if (CameraList.SelectedItem is not LocalCameraDevice camera)
        {
            _selectedCamera = null;
            _cameraService.Stop();
            ClearPreview();
            CaptureStateTextBlock.Text = "SIN SELECCIÓN";
            UpdateActionButtons(null);
            return;
        }

        _selectedCamera = camera;
        SelectedCameraNameText.Text = camera.Name;
        CaptureStateTextBlock.Text = "ABRIENDO";
        NoVideoTextBlock.Visibility = Visibility.Visible;
        NoVideoTextBlock.Text = "ABRIENDO…";
        VideoImage.Source = null;
        UpdateActionButtons(camera);

        StatusTextBlock.Text =
            BuildCameraStatus(camera, "Negociando captura con Media Foundation / DirectShow...");

        try
        {
            // started solo será true después de que OpenCV haya obtenido un frame real.
            var started = await _cameraService.StartAsync(camera);
            if (!started)
            {
                CaptureStateTextBlock.Text = "ERROR";
                NoVideoTextBlock.Visibility = Visibility.Visible;
                NoVideoTextBlock.Text = "SIN SEÑAL";
                StatusTextBlock.Text = BuildCameraStatus(camera, _cameraService.LastCaptureDiagnostic);
                UpdateActionButtons(camera);
                return;
            }

            CaptureStateTextBlock.Text = "STREAM ACTIVO";
            NoVideoTextBlock.Visibility = Visibility.Collapsed;
            StatusTextBlock.Text = BuildCameraStatus(camera, _cameraService.LastCaptureDiagnostic);
            UpdateActionButtons(camera);
        }
        catch (OperationCanceledException)
        {
            CaptureStateTextBlock.Text = "CANCELADO";
            StatusTextBlock.Text = BuildCameraStatus(camera, "Apertura cancelada.");
        }
        catch (Exception ex)
        {
            CaptureStateTextBlock.Text = "ERROR";
            NoVideoTextBlock.Visibility = Visibility.Visible;
            NoVideoTextBlock.Text = "ERROR";
            StatusTextBlock.Text = BuildCameraStatus(camera, $"Error al abrir la cámara: {ex.Message}");
            UpdateActionButtons(camera);
        }
    }

    private void CameraService_FrameReady(object? sender, LocalCameraFrame frame)
    {
        // FrameReady puede ejecutarse desde el hilo de captura; WPF debe actualizarse desde Dispatcher.
        Dispatcher.InvokeAsync(() =>
        {
            if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length == 0)
            {
                VideoImage.Source = null;
                NoVideoTextBlock.Visibility = Visibility.Visible;
                NoVideoTextBlock.Text = "SIN SEÑAL";
                return;
            }

            // bitmap convierte BGRA32 en una imagen WPF independiente de OpenCV y LibVLC.
            var bitmap = BitmapSource.Create(
                frame.Width,
                frame.Height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                frame.Pixels,
                frame.Stride);

            bitmap.Freeze();
            VideoImage.Source = bitmap;
            NoVideoTextBlock.Visibility = Visibility.Collapsed;
            CaptureStateTextBlock.Text = "STREAM ACTIVO";
        });
    }

    private void SnapshotButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_cameraService.IsCapturing)
            return;

        // dialog permite guardar el último frame válido como PNG sin manejar rutas manualmente.
        var dialog = new SaveFileDialog
        {
            Title = "Guardar snapshot de cámara local",
            Filter = "Imagen PNG (*.png)|*.png",
            FileName = $"CameraInspector_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var saved = _cameraService.TakeSnapshot(dialog.FileName);
        StatusTextBlock.Text = BuildCameraStatus(
            _selectedCamera,
            saved ? $"Snapshot guardado: {dialog.FileName}" : _cameraService.LastCaptureDiagnostic);
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_cameraService.IsCapturing || _cameraService.IsRecording)
            return;

        // La grabación local se almacena en AVI/MJPEG para priorizar compatibilidad sin depender de un codec H.264.
        var dialog = new SaveFileDialog
        {
            Title = "Guardar grabación de cámara local",
            Filter = "Video AVI MJPG (*.avi)|*.avi",
            FileName = $"CameraInspector_{DateTime.Now:yyyyMMdd_HHmmss}.avi",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var started = _cameraService.StartRecording(dialog.FileName);
        StatusTextBlock.Text = BuildCameraStatus(
            _selectedCamera,
            started ? $"● GRABANDO · {dialog.FileName}" : _cameraService.LastCaptureDiagnostic);
        UpdateActionButtons(_selectedCamera);
    }

    private void StopRecordButton_Click(object sender, RoutedEventArgs e)
    {
        _cameraService.StopRecording();
        StatusTextBlock.Text = BuildCameraStatus(_selectedCamera, "Grabación detenida.");
        UpdateActionButtons(_selectedCamera);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _cameraService.Stop();
        ClearPreview();
        CaptureStateTextBlock.Text = "DETENIDO";
        StatusTextBlock.Text = BuildCameraStatus(_selectedCamera, "Captura detenida.");
        UpdateActionButtons(_selectedCamera);
    }

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCamera is null)
            return;

        // info contiene únicamente metadatos del dispositivo; nunca incluye credenciales.
        var info = new StringBuilder()
            .AppendLine($"Nombre: {_selectedCamera.Name}")
            .AppendLine($"Origen: {_selectedCamera.DiscoverySource}")
            .AppendLine($"Transporte: {_selectedCamera.Transport}")
            .AppendLine($"Estado: {_selectedCamera.Status}")
            .AppendLine($"VID: {_selectedCamera.UsbVendorId ?? "N/D"}")
            .AppendLine($"PID: {_selectedCamera.UsbProductId ?? "N/D"}")
            .AppendLine($"Índice de captura: {_selectedCamera.CaptureIndex}")
            .AppendLine($"Preview declarado: {_selectedCamera.PreviewSupported}")
            .AppendLine($"DevicePath: {_selectedCamera.DevicePath ?? "N/D"}")
            .AppendLine($"Moniker: {_selectedCamera.MonikerString ?? "N/D"}")
            .AppendLine()
            .AppendLine(_cameraService.LastCaptureDiagnostic)
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
        // captureActive exige una cámara actualmente abierta; una mera enumeración no habilita captura.
        var captureActive = camera is not null && _cameraService.IsCapturing;
        SnapshotButton.IsEnabled = captureActive;
        RecordButton.IsEnabled = captureActive && !_cameraService.IsRecording;
        StopRecordButton.IsEnabled = _cameraService.IsRecording;
        StopButton.IsEnabled = captureActive;
        InfoButton.IsEnabled = camera is not null;
    }

    private void ClearPreview()
    {
        VideoImage.Source = null;
        NoVideoTextBlock.Visibility = Visibility.Visible;
        NoVideoTextBlock.Text = "SIN SEÑAL";
    }

    private static string BuildCameraStatus(LocalCameraDevice? camera, string message)
    {
        if (camera is null)
            return message;

        return $"Origen: {camera.DiscoverySource} · Transporte: {camera.Transport} · " +
               $"VID: {camera.UsbVendorId ?? "N/D"} · PID: {camera.UsbProductId ?? "N/D"}\n" +
               $"Estado Windows: {camera.Status}\n{message}";
    }
}
