using System.Text;
using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Video;
using Microsoft.Win32;

namespace CameraInspector.App;

/// <summary>
/// Reproductor independiente para cámaras IP/RTSP.
/// Mantiene el mismo patrón operativo que la ventana de cámaras USB: preview, snapshot,
/// grabación, detener grabación, detener preview e información.
/// </summary>
public partial class IpCameraVideoWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IVideoPlayerService _videoPlayerService;
    private bool _automaticVideoAttemptStarted;

    public IpCameraVideoWindow(
        MainViewModel viewModel,
        IVideoPlayerService videoPlayerService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(videoPlayerService);

        InitializeComponent();

        _viewModel = viewModel;
        _videoPlayerService = videoPlayerService;
        DataContext = _viewModel;

        VideoSurface.MediaPlayer = _videoPlayerService.Player;
        Closed += IpCameraVideoWindow_Closed;
        Loaded += IpCameraVideoWindow_Loaded;
    }

    private async void IpCameraVideoWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshButtons();

        if (_automaticVideoAttemptStarted || _viewModel.SelectedDevice is null)
            return;

        _automaticVideoAttemptStarted = true;
        try
        {
            await _viewModel.TryStartIpVideoAutomaticallyAsync();
        }
        catch (OperationCanceledException)
        {
            _viewModel.StatusText = "Inicio automático del video cancelado.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"No se pudo iniciar automáticamente el video: {ex.Message}";
        }
        finally
        {
            RefreshCredentialsButton();
            RefreshButtons();
        }
    }

    private void IpCameraVideoWindow_Closed(object? sender, EventArgs e)
    {
        try { _viewModel.StopRecording(); } catch { }
        try { _videoPlayerService.Stop(); } catch { }
        try { VideoSurface.MediaPlayer = null; } catch { }
    }

    private void SnapshotButton_Click(object sender, RoutedEventArgs e)
    {
        var device = _viewModel.SelectedDevice;
        if (device is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar captura de cámara IP",
            Filter = "Imagen PNG (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"CameraIP_{device.IpAddress.Replace('.', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var saved = _videoPlayerService.TakeSnapshot(dialog.FileName);
            _viewModel.StatusText = saved
                ? $"Captura guardada: {dialog.FileName}"
                : "No hay un frame de video disponible para capturar.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"No se pudo guardar la captura: {ex.Message}";
        }
    }

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        var device = _viewModel.SelectedDevice;
        if (device is null || _viewModel.ResolvedMainStream is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar grabación de cámara IP",
            Filter = "Video MP4 (*.mp4)|*.mp4",
            DefaultExt = ".mp4",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"grabacion_{device.IpAddress.Replace('.', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            RecordButton.IsEnabled = false;
            var started = await _viewModel.StartRecordingAsync(dialog.FileName);
            if (!started)
            {
                MessageBox.Show(
                    this,
                    _viewModel.StatusText,
                    "Camera Inspector — Grabación",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                _viewModel.StatusText = $"Grabación iniciada: {dialog.FileName}";
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"No se pudo iniciar la grabación: {ex.Message}";
        }
        finally
        {
            RefreshButtons();
        }
    }

    private void StopRecordButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StopRecording();
        _viewModel.StatusText = "Grabación finalizada y archivo MP4 cerrado correctamente.";
        RefreshButtons();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        try { _viewModel.StopRecording(); } catch { }
        try { _videoPlayerService.Stop(); } catch { }
        _viewModel.StatusText = "Preview de cámara IP detenido.";
        RefreshButtons();
    }

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        var device = _viewModel.SelectedDevice;
        if (device is null)
            return;

        var info = new StringBuilder()
            .AppendLine($"IP: {device.IpAddress}")
            .AppendLine($"MAC: {device.MacAddress}")
            .AppendLine($"Fabricante: {device.Manufacturer}")
            .AppendLine($"Modelo: {device.Model}")
            .AppendLine($"Firmware: {device.Firmware}")
            .AppendLine($"Serial: {device.SerialNumber}")
            .AppendLine($"Estado: {device.Status}")
            .AppendLine($"ONVIF: {device.OnvifSupported}")
            .AppendLine($"RTSP: {device.RtspSupported}")
            .AppendLine($"RTSP principal: {_viewModel.ResolvedMainStream?.RtspUri ?? "No resuelto"}")
            .AppendLine($"RTSP secundario: {_viewModel.ResolvedSubStream?.RtspUri ?? "No resuelto"}")
            .AppendLine()
            .AppendLine("Evidencia:")
            .AppendLine(device.DetectionDetails)
            .ToString();

        MessageBox.Show(
            this,
            info,
            "Camera Inspector — Información de cámara IP",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RefreshButtons()
    {
        var hasDevice = _viewModel.SelectedDevice is not null;
        var hasStream = _viewModel.ResolvedMainStream is not null;
        var playing = _videoPlayerService.Player.IsPlaying;
        var recording = _viewModel.IsRecording;

        MainStreamButton.IsEnabled = hasDevice && !playing && !recording;
        SubStreamButton.IsEnabled = hasDevice && !recording;
        SnapshotButton.IsEnabled = hasDevice && playing;
        RecordButton.IsEnabled = hasDevice && playing && hasStream && !recording;
        StopRecordButton.IsEnabled = recording;
        StopButton.IsEnabled = hasDevice && playing;
        InfoButton.IsEnabled = hasDevice;
    }

    private void RefreshCredentialsButton()
    {
        // La visibilidad/estado real de credenciales se controla desde AuthenticationRequired.
        var button = FindButtonByContent(this, "CREDENCIALES");
        if (button is null)
            return;

        button.IsEnabled = _viewModel.SelectedDevice is not null && _viewModel.AuthenticationRequired;
        button.ToolTip = button.IsEnabled
            ? "La cámara requiere autenticación. Ingrese o actualice usuario y contraseña."
            : "La cámara no requiere credenciales para el acceso actual.";
    }

    private static Button? FindButtonByContent(DependencyObject root, string expectedContent)
    {
        var childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childrenCount; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is Button button &&
                string.Equals(button.Content?.ToString(), expectedContent, StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }

            var nested = FindButtonByContent(child, expectedContent);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
