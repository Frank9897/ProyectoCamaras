using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using DirectShowLib;
using LibVLCSharp.Shared;

namespace CameraInspector.Video;

/// <summary>
/// Enumeración y captura de cámaras locales de Windows mediante DirectShow + LibVLC.
/// PnP se utiliza como respaldo para diagnóstico cuando Windows conoce la cámara pero DirectShow no la expone.
/// </summary>
public sealed class LocalCameraService : ILocalCameraService
{
    private readonly LibVLC _libVlc;
    private Media? _currentMedia;
    private MediaPlayer? _currentPlayer;
    private Media? _recordingMedia;
    private MediaPlayer? _recordingPlayer;
    private string? _currentCameraName;

    /// <summary>Notifica a la UI qué MediaPlayer debe mostrar o retirar.</summary>
    public event EventHandler<MediaPlayer?>? PlayerChanged;

    /// <summary>Reproductor local actualmente activo.</summary>
    public MediaPlayer? Player => _currentPlayer;

    /// <summary>Diagnóstico de la última enumeración local ejecutada.</summary>
    public string LastEnumerationDiagnostic { get; private set; } = "Todavía no se ejecutó una enumeración local.";

    /// <summary>Indica si existe una grabación local activa.</summary>
    public bool IsRecording => _recordingPlayer?.IsPlaying == true;

    /// <summary>Indica cuántas salidas de vídeo tiene la previsualización actual.</summary>
    public uint VideoOutputCount => _currentPlayer?.VoutCount ?? 0;

    public LocalCameraService()
    {
        // Inicializamos el motor multimedia local para captura DirectShow.
        global::LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC("--quiet", "--live-caching=100");
    }

    public IReadOnlyList<LocalCameraDevice> GetAvailableCameras()
    {
        var cameras = new List<LocalCameraDevice>();
        var diagnostics = new List<string>();

        try
        {
            // devices contiene las fuentes de vídeo registradas en DirectShow.
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            foreach (var device in devices)
            {
                try
                {
                    // name es el nombre amigable que Windows registra para la cámara.
                    var name = device.Name;
                    // devicePath identifica la instancia física cuando el driver la proporciona.
                    var devicePath = device.DevicePath;
                    // monikerString queda disponible para diagnóstico técnico.
                    var monikerString = device.Mon?.ToString();

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // transport clasifica el origen como USB o local/virtual.
                    var transport = string.IsNullOrWhiteSpace(devicePath)
                        ? "Local/Virtual"
                        : devicePath.Contains("usb#vid_", StringComparison.OrdinalIgnoreCase)
                            ? "USB"
                            : "Local/Virtual";

                    // usbVendorId contiene el VID cuando el DevicePath lo expone.
                    var usbVendorId = ExtractUsbId(devicePath, "vid_");
                    // usbProductId contiene el PID cuando el DevicePath lo expone.
                    var usbProductId = ExtractUsbId(devicePath, "pid_");

                    cameras.Add(new LocalCameraDevice
                    {
                        Name = name,
                        DevicePath = devicePath,
                        MonikerString = monikerString,
                        DiscoverySource = "DirectShow",
                        PreviewSupported = true,
                        Transport = transport,
                        UsbVendorId = usbVendorId,
                        UsbProductId = usbProductId,
                        IsVideoCaptureDevice = true,
                        Status = "Disponible"
                    });
                }
                catch (Exception ex)
                {
                    // Un driver defectuoso no debe impedir enumerar las demás cámaras.
                    diagnostics.Add($"DirectShow [{device.Name}]: {ex.Message}");
                }
                finally
                {
                    // DirectShow utiliza COM; liberamos la referencia al terminar con el dispositivo.
                    ReleaseComObject(device);
                }
            }
        }
        catch (Exception ex)
        {
            // Conservamos el error para mostrarlo al técnico en lugar de ocultarlo.
            diagnostics.Add($"DirectShow: {ex.Message}");
        }

        // Si DirectShow no encontró nada, consultamos PnP como respaldo de diagnóstico.
        if (cameras.Count == 0)
        {
            var pnpCameras = GetPnpCameraDevices(out var pnpDiagnostic);
            foreach (var pnpCamera in pnpCameras)
            {
                // Evitamos duplicados por nombre cuando ambos enumeradores devuelven la misma fuente.
                if (!cameras.Any(item => string.Equals(item.Name, pnpCamera.Name, StringComparison.OrdinalIgnoreCase)))
                    cameras.Add(pnpCamera);
            }

            if (!string.IsNullOrWhiteSpace(pnpDiagnostic))
                diagnostics.Add(pnpDiagnostic);
        }

        LastEnumerationDiagnostic = cameras.Count > 0
            ? $"Enumeración local completada: {cameras.Count} fuente(s)."
            : diagnostics.Count == 0
                ? "Windows no devolvió ninguna fuente de captura de vídeo local."
                : string.Join(" | ", diagnostics);

        return cameras
            .OrderBy(camera => camera.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LocalCameraDevice> GetPnpCameraDevices(out string diagnostic)
    {
        var result = new List<LocalCameraDevice>();
        diagnostic = string.Empty;

        try
        {
            // startInfo ejecuta solo una consulta fija de lectura de dispositivos PnP presentes.
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // ArgumentList separa los argumentos y evita errores de escapado.
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(
                "Get-PnpDevice -PresentOnly | " +
                "Where-Object { $_.Status -eq 'OK' -and ($_.Class -eq 'Camera' -or $_.Class -eq 'Image' -or $_.Service -eq 'usbvideo') } | " +
                "Select-Object FriendlyName,InstanceId,Class,Status,Service | ConvertTo-Json -Compress");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                diagnostic = "PnP: no se pudo iniciar PowerShell.";
                return result;
            }

            // output contiene la respuesta JSON de la consulta PnP.
            var output = process.StandardOutput.ReadToEnd();
            // error contiene el detalle de error emitido por PowerShell, si existe.
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(3000);

            if (string.IsNullOrWhiteSpace(output))
            {
                diagnostic = string.IsNullOrWhiteSpace(error)
                    ? "PnP: Windows no devolvió cámaras presentes."
                    : $"PnP: {error.Trim()}";
                return result;
            }

            if (output.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                var devices = JsonSerializer.Deserialize<List<PnpCameraDto>>(output);
                if (devices is not null)
                    AddPnpDevices(result, devices);
            }
            else
            {
                var device = JsonSerializer.Deserialize<PnpCameraDto>(output);
                if (device is not null)
                    AddPnpDevices(result, new[] { device });
            }
        }
        catch (Exception ex)
        {
            diagnostic = $"PnP: {ex.Message}";
        }

        return result;
    }

    private static void AddPnpDevices(
        ICollection<LocalCameraDevice> destination,
        IEnumerable<PnpCameraDto> devices)
    {
        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.FriendlyName))
                continue;

            // instanceId identifica el dispositivo PnP aunque DirectShow no tenga una fuente utilizable.
            var instanceId = device.InstanceId;
            // isUsb permite informar el transporte inferido a partir del identificador PnP.
            var isUsb = instanceId?.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) == true;

            destination.Add(new LocalCameraDevice
            {
                Name = device.FriendlyName.Trim(),
                DevicePath = instanceId,
                DiscoverySource = "Windows PnP",
                PreviewSupported = false,
                Transport = isUsb ? "USB" : "Local/Virtual",
                UsbVendorId = ExtractUsbId(instanceId, "vid_"),
                UsbProductId = ExtractUsbId(instanceId, "pid_"),
                IsVideoCaptureDevice = true,
                Status = string.IsNullOrWhiteSpace(device.Status) ? "Disponible" : device.Status
            });
        }
    }

    public async Task<bool> PlayAsync(LocalCameraDevice camera, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(camera);

        // Una única previsualización local evita conflictos con drivers que no permiten uso compartido.
        Stop();

        if (!camera.PreviewSupported)
            return false;

        // Algunas webcams aceptan solo determinadas combinaciones de resolución/FPS; probamos varias.
        var sizes = new string?[] { "640x480", "1280x720", null };

        foreach (var size in sizes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var media = new Media(_libVlc, "dshow://", FromType.FromLocation);
            // dshow-vdev selecciona exactamente la fuente registrada por DirectShow.
            media.AddOption($":dshow-vdev=\"{EscapeOption(camera.Name)}\"");
            // No abrimos audio durante esta etapa para simplificar la negociación del dispositivo.
            media.AddOption(":dshow-adev=none");
            // Evitamos diálogos interactivos del driver.
            media.AddOption(":no-dshow-config");
            media.AddOption(":no-dshow-tuner");
            // caching bajo para una previsualización con poca latencia.
            media.AddOption(":live-caching=100");

            if (!string.IsNullOrWhiteSpace(size))
            {
                // dshow-size fuerza una resolución inicial habitual.
                media.AddOption($":dshow-size={size}");
                media.AddOption(":dshow-fps=30");
            }

            var player = new MediaPlayer(_libVlc);
            var videoOutputCreated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // voutHandler detecta la creación real de una salida de vídeo, no solo el inicio solicitado por Play().
            EventHandler<MediaPlayerVoutEventArgs>? voutHandler = (_, args) =>
            {
                if (args.Count > 0)
                    videoOutputCreated.TrySetResult(true);
            };

            // errorHandler marca un fallo de reproducción y evita informar una captura que realmente no arrancó.
            EventHandler<EventArgs>? errorHandler = (_, _) => videoOutputCreated.TrySetResult(false);

            player.Vout += voutHandler;
            player.EncounteredError += errorHandler;

            try
            {
                // started solo indica que LibVLC aceptó el medio; VoutCount confirma que existe salida de vídeo.
                var started = player.Play(media);
                if (!started)
                    videoOutputCreated.TrySetResult(false);
                else if (player.VoutCount > 0)
                    videoOutputCreated.TrySetResult(true);

                var hasVideoOutput = await WaitForVideoOutputAsync(
                    videoOutputCreated.Task,
                    cancellationToken);

                if (hasVideoOutput)
                {
                    _currentMedia = media;
                    _currentPlayer = player;
                    _currentCameraName = camera.Name;
                    PlayerChanged?.Invoke(this, _currentPlayer);
                    return true;
                }
            }
            finally
            {
                player.Vout -= voutHandler;
                player.EncounteredError -= errorHandler;

                if (!ReferenceEquals(player, _currentPlayer))
                {
                    if (player.IsPlaying)
                        player.Stop();

                    player.Dispose();
                    media.Dispose();
                }
            }
        }

        PlayerChanged?.Invoke(this, null);
        return false;
    }

    private static async Task<bool> WaitForVideoOutputAsync(
        Task<bool> outputTask,
        CancellationToken cancellationToken)
    {
        // timeout evita que un driver que no entrega frames bloquee indefinidamente la aplicación.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await outputTask.WaitAsync(linkedCts.Token);
            return outputTask.IsCompletedSuccessfully && outputTask.Result;
        }
        catch (OperationCanceledException) when (
            timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public void Stop()
    {
        if (_currentPlayer is not null)
        {
            // IsPlaying indica si la previsualización continúa reproduciéndose.
            if (_currentPlayer.IsPlaying)
                _currentPlayer.Stop();

            _currentPlayer.Dispose();
            _currentPlayer = null;
        }

        _currentMedia?.Dispose();
        _currentMedia = null;
        _currentCameraName = null;

        PlayerChanged?.Invoke(this, null);
    }

    public bool TakeSnapshot(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || _currentPlayer is null || VideoOutputCount == 0)
            return false;

        // directory garantiza que la carpeta elegida exista antes de pedir el snapshot a LibVLC.
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        // TakeSnapshot usa la primera salida de vídeo y conserva su resolución original con 0x0.
        return _currentPlayer.TakeSnapshot(0, filePath, 0, 0);
    }

    public bool StartRecording(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(_currentCameraName) || VideoOutputCount == 0)
            return false;

        StopRecording();

        // recordingMedia abre una segunda fuente DirectShow para no interrumpir la previsualización.
        _recordingMedia = new Media(_libVlc, "dshow://", FromType.FromLocation);
        _recordingMedia.AddOption($":dshow-vdev=\"{EscapeOption(_currentCameraName)}\"");
        _recordingMedia.AddOption(":dshow-adev=none");
        _recordingMedia.AddOption(":dshow-size=640x480");
        _recordingMedia.AddOption(":dshow-fps=30");
        _recordingMedia.AddOption(":no-dshow-config");
        _recordingMedia.AddOption(
            $":sout=#transcode{{vcodec=h264,vb=2500,acodec=none}}:std{{access=file,mux=mp4,dst=\"{EscapeSoutPath(filePath)}\"}}");
        _recordingMedia.AddOption(":sout-keep");

        _recordingPlayer = new MediaPlayer(_libVlc);
        var started = _recordingPlayer.Play(_recordingMedia);
        if (!started)
        {
            StopRecording();
            return false;
        }

        return true;
    }

    public void StopRecording()
    {
        if (_recordingPlayer is not null)
        {
            // IsPlaying indica si el reproductor de grabación sigue activo.
            if (_recordingPlayer.IsPlaying)
                _recordingPlayer.Stop();

            _recordingPlayer.Dispose();
            _recordingPlayer = null;
        }

        _recordingMedia?.Dispose();
        _recordingMedia = null;
    }

    public void Dispose()
    {
        StopRecording();
        Stop();
        _libVlc.Dispose();
    }

    private static string? ExtractUsbId(string? devicePath, string token)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            return null;

        // match localiza exactamente cuatro dígitos hexadecimales después de VID_ o PID_.
        var match = Regex.Match(
            devicePath,
            $@"{Regex.Escape(token)}([0-9a-fA-F]{{4}})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static string EscapeOption(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string EscapeSoutPath(string value) =>
        value.Replace("\\", "/", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void ReleaseComObject(object instance)
    {
        if (!Marshal.IsComObject(instance))
            return;

        try
        {
            Marshal.FinalReleaseComObject(instance);
        }
        catch
        {
            // Algunos drivers tienen una liberación COM defectuosa; no propagamos ese error a la aplicación.
        }
    }

    /// <summary>DTO mínimo usado exclusivamente para interpretar Get-PnpDevice.</summary>
    private sealed class PnpCameraDto
    {
        public string? FriendlyName { get; set; }
        public string? InstanceId { get; set; }
        public string? Class { get; set; }
        public string? Status { get; set; }
        public string? Service { get; set; }
    }
}
