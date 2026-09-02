using System.Diagnostics;
using System.Text;
using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Video;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace CameraInspector.App;

/// <summary>
/// Reproductor independiente para cámaras IP/RTSP.
/// Los controles permanecen visibles aunque la cámara falle: la UI informa qué servicio no responde,
/// qué requiere credenciales y qué operación no está disponible.
/// </summary>
public partial class IpCameraVideoWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IVideoPlayerService _videoPlayerService;
    private bool _automaticVideoAttemptStarted;

    public IpCameraVideoWindow(MainViewModel viewModel, IVideoPlayerService videoPlayerService)
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
        RefreshCredentialsButton();

        if (_automaticVideoAttemptStarted || _viewModel.SelectedDevice is null)
            return;

        _automaticVideoAttemptStarted = true;
        try
        {
            await _viewModel.RecheckSelectedHealthAsync();
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

    private async void HealthButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HealthButton.IsEnabled = false;
            _viewModel.StatusText = "Comprobando comunicación y vídeo...";
            await _viewModel.RecheckSelectedHealthAsync();
            _viewModel.StatusText = _viewModel.SelectedDevice?.HealthMessage ?? "Comprobación finalizada.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"No se pudo comprobar la salud: {ex.Message}";
        }
        finally
        {
            HealthButton.IsEnabled = true;
            RefreshCredentialsButton();
            RefreshButtons();
        }
    }

    private async void NetworkButton_Click(object sender, RoutedEventArgs e)
    {
        var device = _viewModel.SelectedDevice;
        if (device is null)
            return;

        if (!device.OnvifSupported && !device.HasMediaService)
        {
            _viewModel.StatusText = "Configuración de red no disponible: esta cámara no anunció administración ONVIF.";
            return;
        }

        try
        {
            var services = App.Services;
            if (services is null)
                return;

            var window = new NetworkConfigurationWindow(
                device,
                services.GetRequiredService<IOnvifDeviceService>(),
                services.GetRequiredService<ICredentialStore>(),
                services.GetRequiredService<ICameraCredentialStore>())
            {
                Owner = this,
                ShowInTaskbar = false
            };
            window.ShowDialog();
            await _viewModel.RecheckSelectedHealthAsync();
            RefreshButtons();
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"No se pudo abrir configuración de red: {ex.Message}";
        }
    }

    private void OpenWebButton_Click(object sender, RoutedEventArgs e)
    {
        var ip = _viewModel.SelectedDevice?.IpAddress;
        if (string.IsNullOrWhiteSpace(ip))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"http://{ip}",
                UseShellExecute = true
            });
            _viewModel.StatusText = $"Abriendo interfaz web de {ip}...";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"No se pudo abrir la interfaz web: {ex.Message}";
        }
    }

    private void CopyIpButton_Click(object sender, RoutedEventArgs e)
    {
        var ip = _viewModel.SelectedDevice?.IpAddress;
        if (string.IsNullOrWhiteSpace(ip))
            return;

        try
        {
            Clipboard.SetText(ip);
            _viewModel.StatusText = $"IP copiada al portapapeles: {ip}";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"No se pudo copiar la IP: {ex.Message}";
        }
    }

    private async void CredentialsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.SaveCredentialsCommand.CanExecute(null))
                await _viewModel.SaveCredentialsCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"No se pudieron gestionar las credenciales: {ex.Message}";
        }
        finally
        {
            RefreshCredentialsButton();
            RefreshButtons();
        }
    }

    private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.RunDiagnosticsCommand.CanExecute(null))
                await _viewModel.RunDiagnosticsCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"No se pudo ejecutar el diagnóstico: {ex.Message}";
        }
        finally
        {
            RefreshButtons();
        }
    }

    private async Task MovePtzAsync(OnvifPtzMoveRequest request, string description)
    {
        var device = _viewModel.SelectedDevice;
        if (device is null)
            return;

        if (!device.HasPtzService)
        {
            _viewModel.StatusText = "PTZ no disponible: la cámara no anunció servicio PTZ ONVIF.";
            return;
        }

        var service = App.Services?.GetService<IOnvifPtzService>();
        if (service is null)
        {
            _viewModel.StatusText = "Servicio PTZ no registrado.";
            return;
        }

        try
        {
            var credentials = await _viewModel.RequestCredentialsForOperationAsync();
            if (credentials is null)
                return;

            var success = await service.ContinuousMoveAsync(
                device.Device,
                request,
                credentials.Value.Username,
                credentials.Value.Password);

            _viewModel.StatusText = success
                ? $"PTZ: {description}."
                : $"PTZ no disponible: la cámara rechazó o no respondió al movimiento ({description}).";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error PTZ ({description}): {ex.Message}";
        }
    }

    private async void PtzUp_Click(object sender, RoutedEventArgs e) => await MovePtzAsync(new OnvifPtzMoveRequest { Tilt = 0.65f }, "arriba");
    private async void PtzDown_Click(object sender, RoutedEventArgs e) => await MovePtzAsync(new OnvifPtzMoveRequest { Tilt = -0.65f }, "abajo");
    private async void PtzLeft_Click(object sender, RoutedEventArgs e) => await MovePtzAsync(new OnvifPtzMoveRequest { Pan = -0.65f }, "izquierda");
    private async void PtzRight_Click(object sender, RoutedEventArgs e) => await MovePtzAsync(new OnvifPtzMoveRequest { Pan = 0.65f }, "derecha");
    private async void PtzZoomIn_Click(object sender, RoutedEventArgs e) => await MovePtzAsync(new OnvifPtzMoveRequest { Zoom = 0.65f }, "zoom +");
    private async void PtzZoomOut_Click(object sender, RoutedEventArgs e) => await MovePtzAsync(new OnvifPtzMoveRequest { Zoom = -0.65f }, "zoom -");

    private async void PtzStop_Click(object sender, RoutedEventArgs e)
    {
        var device = _viewModel.SelectedDevice;
        if (device is null)
            return;

        var service = App.Services?.GetService<IOnvifPtzService>();
        if (service is null || !device.HasPtzService)
        {
            _viewModel.StatusText = "PTZ no disponible para esta cámara.";
            return;
        }

        try
        {
            var credentials = await _viewModel.RequestCredentialsForOperationAsync();
            if (credentials is null)
                return;

            var success = await service.StopAsync(device.Device, credentials.Value.Username, credentials.Value.Password);
            _viewModel.StatusText = success ? "PTZ detenido." : "La cámara no confirmó la detención del PTZ.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error al detener PTZ: {ex.Message}";
        }
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
                MessageBox.Show(this, _viewModel.StatusText, "Camera Inspector — Grabación", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            .AppendLine($"Salud: {device.HealthDisplay}")
            .AppendLine($"Comunicación: {device.CommunicationDisplay}")
            .AppendLine($"Video: {device.VideoDisplay}")
            .AppendLine($"Alerta: {device.AlertDisplay}")
            .AppendLine($"Puerto comunicación: {device.CommunicationPort?.ToString() ?? "—"}")
            .AppendLine($"Protocolo comunicación: {device.CommunicationProtocol}")
            .AppendLine($"ONVIF: {device.OnvifSupported}")
            .AppendLine($"RTSP: {device.RtspSupported}")
            .AppendLine($"RTSP principal: {_viewModel.ResolvedMainStream?.RtspUri ?? "No resuelto"}")
            .AppendLine($"RTSP secundario: {_viewModel.ResolvedSubStream?.RtspUri ?? "No resuelto"}")
            .AppendLine()
            .AppendLine("Mensaje de salud:")
            .AppendLine(device.HealthMessage)
            .AppendLine()
            .AppendLine("Evidencia:")
            .AppendLine(device.DetectionDetails)
            .ToString();

        MessageBox.Show(this, info, "Camera Inspector — Información de cámara IP", MessageBoxButton.OK, MessageBoxImage.Information);
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
        HealthButton.IsEnabled = hasDevice;
    }

    private void RefreshCredentialsButton()
    {
        // El control permanece visible y disponible aunque todavía no se haya detectado la necesidad de autenticación.
        CredentialsButton.IsEnabled = _viewModel.SelectedDevice is not null;
        CredentialsButton.ToolTip = _viewModel.AuthenticationRequired
            ? "La cámara solicita autenticación. Ingrese o actualice usuario y contraseña."
            : "Administrar credenciales guardadas para esta cámara.";
    }
}
