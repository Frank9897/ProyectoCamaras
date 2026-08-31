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
    private WriteableBitmap? _previewBitmap;
    private LocalCameraFrame? _pendingFrame;
    private bool _frameDispatchPending;
    private readonly object _frameSync = new();
    private long _displayedFrames;

    public LocalCamerasWindow(LocalCameraService cameraService)
    {
        ArgumentNullException.ThrowIfNull(cameraService);

        InitializeComponent();

        _cameraService = cameraService;
        _cameraService.FrameReady += CameraService_FrameReady;

        Loaded += (_, _) => RefreshCameras();
        Closed += (_, _) =>
        {
            _cameraService.FrameReady -= CameraService_FrameReady;
            _cameraService.StopRecording();
            _cameraService.Stop();
            ClearPendingFrame();
        };

        UpdateActionButtons(null);
    }

    /// <summary>
    /// Actualiza el listado cuando esta misma vista está embebida dentro de una pestaña de MainWindow.
    /// </summary>
    public void RefreshEmbedded()
    {
        RefreshCameras();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshCameras();

    private void RefreshCameras()
    {
        try
        {
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
        if (CameraList.SelectedItem is not LocalCameraDevice camera)
        {
            _selectedCamera = null;
            _cameraService.Stop();
            ClearPreview();
            CaptureStateTextBlock.Text = "SIN SELECCIÓN";
            UpdateActionButtons(null);
            return;
        }

        _cameraService.StopRecording();
        _selectedCamera = camera;
        SelectedCameraNameText.Text = camera.Name;
        CaptureStateTextBlock.Text = "ABRIENDO";
        NoVideoTextBlock.Visibility = Visibility.Visible;
        NoVideoTextBlock.Text = "NEGOCIANDO…";
        ResetPreviewBitmap();
        ClearPendingFrame();
        UpdateActionButtons(camera);

        StatusTextBlock.Text =
            BuildCameraStatus(camera, "Negociando captura con DirectShow / Media Foundation...");

        try
        {
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

            CaptureStateTextBlock.Text = "RECIBIENDO";
            NoVideoTextBlock.Visibility = Visibility.Visible;
            NoVideoTextBlock.Text = "PINTANDO PREVIEW…";
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
        var scheduleRender = false;

        lock (_frameSync)
        {
            _pendingFrame = frame;

            if (!_frameDispatchPending)
            {
                _frameDispatchPending = true;
                scheduleRender = true;
            }
        }

        if (!scheduleRender)
            return;

        Dispatcher.BeginInvoke(
            new Action(RenderPendingFrame),
            System.Windows.Threading.DispatcherPriority.Render);
    }

    private void RenderPendingFrame()
    {
        LocalCameraFrame? frame;

        lock (_frameSync)
        {
            frame = _pendingFrame;
            _pendingFrame = null;
            _frameDispatchPending = false;
        }

        if (frame is null)
            return;

        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length == 0)
        {
            ResetPreviewBitmap();
            NoVideoTextBlock.Visibility = Visibility.Visible;
            NoVideoTextBlock.Text = "SIN SEÑAL";
            CaptureStateTextBlock.Text = "DETENIDO";
            return;
        }

        if (_previewBitmap is null ||
            _previewBitmap.PixelWidth != frame.Width ||
            _previewBitmap.PixelHeight != frame.Height)
        {
            _previewBitmap = new WriteableBitmap(
                frame.Width,
                frame.Height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null);
            VideoImage.Source = _previewBitmap;
        }

        _previewBitmap.WritePixels(
            new Int32Rect(0, 0, frame.Width, frame.Height),
            frame.Pixels,
            frame.Stride,
            0);

        _displayedFrames++;
        CaptureStateTextBlock.Text = "STREAM ACTIVO";
        NoVideoTextBlock.Visibility = Visibility.Collapsed;

        if (_displayedFrames == 1 || _displayedFrames % 30 == 0)
        {
            StatusTextBlock.Text = BuildCameraStatus(
                _selectedCamera,
                $"{_cameraService.LastCaptureDiagnostic} · Frames pintados en WPF: {_displayedFrames}.");
        }

        lock (_frameSync)
        {
            if (_pendingFrame is not null && !_frameDispatchPending)
            {
                _frameDispatchPending = true;
                Dispatcher.BeginInvoke(
                    new Action(RenderPendingFrame),
                    System.Windows.Threading.DispatcherPriority.Render);
            }
        }
    }

    private void SnapshotButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_cameraService.IsCapturing)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar snapshot de cámara local",
            Filter = "Imagen PNG (*.png)|*.png",
            FileName = $"CameraInspector_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (!ShowSaveDialog(dialog))
            return;

        try
        {
            var saved = _cameraService.TakeSnapshot(dialog.FileName);
            StatusTextBlock.Text = BuildCameraStatus(
                _selectedCamera,
                saved ? $"Snapshot guardado: {dialog.FileName}" : _cameraService.LastCaptureDiagnostic);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = BuildCameraStatus(_selectedCamera, $"Error al guardar snapshot: {ex.Message}");
            MessageBox.Show(
                this is { IsVisible: true } ? this : Application.Current.MainWindow,
                $"No se pudo guardar el snapshot:\n\n{ex.Message}",
                "Camera Inspector — Snapshot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_cameraService.IsCapturing || _cameraService.IsRecording)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar grabación de cámara local",
            Filter = "Video AVI MJPG (*.avi)|*.avi",
            FileName = $"CameraInspector_{DateTime.Now:yyyyMMdd_HHmmss}.avi",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (!ShowSaveDialog(dialog))
            return;

        try
        {
            var started = _cameraService.StartRecording(dialog.FileName);
            StatusTextBlock.Text = BuildCameraStatus(
                _selectedCamera,
                started ? $"● GRABANDO · {dialog.FileName}" : _cameraService.LastCaptureDiagnostic);
            UpdateActionButtons(_selectedCamera);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = BuildCameraStatus(_selectedCamera, $"Error al iniciar grabación: {ex.Message}");
            MessageBox.Show(
                this is { IsVisible: true } ? this : Application.Current.MainWindow,
                $"No se pudo iniciar la grabación:\n\n{ex.Message}",
                "Camera Inspector — Grabación",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool ShowSaveDialog(SaveFileDialog dialog)
    {
        // Una LocalCamerasWindow embebida no tiene HWND propio visible y no puede ser owner de CommonDialog.
        // En ese caso usamos MainWindow; cuando la vista es una ventana real y visible, usamos this.
        if (IsVisible && IsLoaded)
            return dialog.ShowDialog(this) == true;

        if (Application.Current.MainWindow is { IsLoaded: true, IsVisible: true } owner)
            return dialog.ShowDialog(owner) == true;

        // Último fallback para contextos de arranque/cierre donde no existe una Window válida como owner.
        return dialog.ShowDialog() == true;
    }

    private void StopRecordButton_Click(object sender, RoutedEventArgs e)
    {
        _cameraService.StopRecording();
        StatusTextBlock.Text = BuildCameraStatus(_selectedCamera, "Grabación detenida.");
        UpdateActionButtons(_selectedCamera);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _cameraService.StopRecording();
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

        var owner = this is { IsVisible: true } ? this : Application.Current.MainWindow;

        MessageBox.Show(
            owner,
            info,
            "Camera Inspector — Información de cámara local",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void UpdateActionButtons(LocalCameraDevice? camera)
    {
        var captureActive = camera is not null && _cameraService.IsCapturing;
        SnapshotButton.IsEnabled = captureActive;
        RecordButton.IsEnabled = captureActive && !_cameraService.IsRecording;
        StopRecordButton.IsEnabled = _cameraService.IsRecording;
        StopButton.IsEnabled = captureActive;
        InfoButton.IsEnabled = camera is not null;
    }

    private void ClearPreview()
    {
        ClearPendingFrame();
        ResetPreviewBitmap();
        NoVideoTextBlock.Visibility = Visibility.Visible;
        NoVideoTextBlock.Text = "SIN SEÑAL";
    }

    private void ClearPendingFrame()
    {
        lock (_frameSync)
        {
            _pendingFrame = null;
            _frameDispatchPending = false;
        }
    }

    private void ResetPreviewBitmap()
    {
        _previewBitmap = null;
        _displayedFrames = 0;
        VideoImage.Source = null;
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
