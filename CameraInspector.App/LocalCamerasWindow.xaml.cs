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
    // _cameraService enumera, captura frames, genera snapshots y controla grabaciones locales.
    private readonly LocalCameraService _cameraService;
    // _selectedCamera conserva el dispositivo actualmente seleccionado en la interfaz.
    private LocalCameraDevice? _selectedCamera;
    // _previewBitmap es un único bitmap reutilizado durante toda la captura mientras no cambie la resolución.
    private WriteableBitmap? _previewBitmap;
    // _pendingFrame conserva solamente el último frame recibido que todavía no se ha pintado.
    private LocalCameraFrame? _pendingFrame;
    // _frameDispatchPending evita llenar Dispatcher con decenas de operaciones si la UI se retrasa.
    private bool _frameDispatchPending;
    // _frameSync protege el buffer pendiente entre el hilo de captura y el hilo de interfaz.
    private readonly object _frameSync = new();
    // _displayedFrames cuenta los frames que realmente llegaron a WriteableBitmap.
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

            // Conservamos el dispositivo previamente seleccionado si todavía existe; de lo contrario usamos el primero.
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
        NoVideoTextBlock.Text = "NEGOCIANDO…";
        ResetPreviewBitmap();
        ClearPendingFrame();
        UpdateActionButtons(camera);

        StatusTextBlock.Text =
            BuildCameraStatus(camera, "Negociando captura con DirectShow / Media Foundation...");

        try
        {
            // started solo significa que OpenCV recibió al menos un frame; el estado visual se confirma al pintarlo.
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

            // No marcamos STREAM ACTIVO aquí: RenderPendingFrame lo hará después de pintar un frame válido.
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
            // Solo conservamos el último frame; cualquier frame intermedio se descarta si la UI está ocupada.
            _pendingFrame = frame;

            // Un solo callback pendiente basta para drenar el último frame disponible.
            if (!_frameDispatchPending)
            {
                _frameDispatchPending = true;
                scheduleRender = true;
            }
        }

        if (!scheduleRender)
            return;

        // BeginInvoke nunca bloquea el hilo de captura esperando a que WPF termine de pintar.
        Dispatcher.BeginInvoke(
            new Action(RenderPendingFrame),
            System.Windows.Threading.DispatcherPriority.Render);
    }

    private void RenderPendingFrame()
    {
        LocalCameraFrame? frame;

        lock (_frameSync)
        {
            // Recuperamos el frame más reciente y liberamos el flag para permitir que lleguen nuevos frames.
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

        // El mismo WriteableBitmap recibe los frames consecutivos, evitando crear objetos WPF a cada captura.
        if (_previewBitmap is null ||
            _previewBitmap.PixelWidth != frame.Width ||
            _previewBitmap.PixelHeight != frame.Height)
        {
            // _previewBitmap se crea mutable porque WritePixels necesita modificar su buffer posteriormente.
            _previewBitmap = new WriteableBitmap(
                frame.Width,
                frame.Height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null);
            VideoImage.Source = _previewBitmap;
        }

        // WritePixels copia únicamente el último frame al buffer ya existente.
        _previewBitmap.WritePixels(
            new Int32Rect(0, 0, frame.Width, frame.Height),
            frame.Pixels,
            frame.Stride,
            0);

        _displayedFrames++;
        CaptureStateTextBlock.Text = "STREAM ACTIVO";
        NoVideoTextBlock.Visibility = Visibility.Collapsed;

        // Solo actualizamos texto cada 30 frames para no provocar repintados de texto innecesarios.
        if (_displayedFrames == 1 || _displayedFrames % 30 == 0)
        {
            StatusTextBlock.Text = BuildCameraStatus(
                _selectedCamera,
                $"{_cameraService.LastCaptureDiagnostic} · Frames pintados en WPF: {_displayedFrames}.");
        }

        // Si llegó otro frame mientras renderizábamos, dejamos un único callback adicional pendiente.
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

        // dialog permite guardar el último frame completo como PNG sin manejar rutas manualmente.
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

        // La grabación local se almacena en AVI/MJPEG para priorizar compatibilidad.
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
        // captureActive exige una cámara actualmente abierta; enumerar el dispositivo no habilita captura.
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
            // Reemplazar por null descarta cualquier frame que aún no haya llegado al renderizador.
            _pendingFrame = null;
            _frameDispatchPending = false;
        }
    }

    private void ResetPreviewBitmap()
    {
        // Liberamos la referencia al bitmap anterior al cambiar de cámara o detener la vista.
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
