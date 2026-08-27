using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using DirectShowLib;
using OpenCvSharp;

namespace CameraInspector.Video;

/// <summary>
/// Captura de cámaras locales en Windows.
/// DirectShow se utiliza para descubrir e identificar dispositivos; OpenCV captura los frames
/// utilizando los backends nativos de Windows, priorizando Media Foundation y usando DirectShow como respaldo.
/// </summary>
public sealed class LocalCameraService : ILocalCameraService
{
    private readonly object _sync = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private Mat? _latestFrame;
    private VideoWriter? _videoWriter;
    private double _captureFps = 30;
    private int _captureWidth;
    private int _captureHeight;

    /// <summary>Emite frames BGRA listos para que WPF los muestre.</summary>
    public event EventHandler<LocalCameraFrame>? FrameReady;

    /// <summary>Diagnóstico de enumeración de cámaras locales.</summary>
    public string LastEnumerationDiagnostic { get; private set; } = "Todavía no se ejecutó una enumeración local.";

    /// <summary>Diagnóstico de la última operación de captura.</summary>
    public string LastCaptureDiagnostic { get; private set; } = "Todavía no se abrió una cámara local.";

    /// <summary>Indica si existe una cámara local abierta.</summary>
    public bool IsCapturing => _capture is not null;

    /// <summary>Indica si actualmente se guardan frames en un archivo local.</summary>
    public bool IsRecording
    {
        get
        {
            lock (_sync)
                return _videoWriter is not null;
        }
    }

    public IReadOnlyList<LocalCameraDevice> GetAvailableCameras()
    {
        var cameras = new List<LocalCameraDevice>();
        var diagnostics = new List<string>();

        try
        {
            // DirectShow proporciona nombres, DevicePath y una enumeración estable para este proceso.
            // El orden se usa como índice candidato de OpenCV; Windows no garantiza que ese índice sea estable entre equipos.
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            for (var index = 0; index < devices.Length; index++)
            {
                var device = devices[index];
                try
                {
                    // name es el FriendlyName registrado por Windows.
                    var name = device.Name;
                    // devicePath conserva la identidad técnica para diagnóstico y futuras mejoras de matching.
                    var devicePath = device.DevicePath;
                    // monikerString permite conservar el identificador COM de DirectShow.
                    var monikerString = device.Mon?.ToString();

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // transport indica si el DevicePath contiene una referencia USB conocida.
                    var transport = string.IsNullOrWhiteSpace(devicePath)
                        ? "Local/Virtual"
                        : devicePath.Contains("usb#vid_", StringComparison.OrdinalIgnoreCase)
                            ? "USB"
                            : "Local/Virtual";

                    // usbVendorId identifica el fabricante USB a partir de VID_.
                    var usbVendorId = ExtractUsbId(devicePath, "vid_");
                    // usbProductId identifica el modelo USB a partir de PID_.
                    var usbProductId = ExtractUsbId(devicePath, "pid_");

                    cameras.Add(new LocalCameraDevice
                    {
                        Name = name,
                        DevicePath = devicePath,
                        MonikerString = monikerString,
                        DiscoverySource = "DirectShow",
                        PreviewSupported = true,
                        CaptureIndex = index,
                        Transport = transport,
                        UsbVendorId = usbVendorId,
                        UsbProductId = usbProductId,
                        IsVideoCaptureDevice = true,
                        Status = "Disponible"
                    });
                }
                catch (Exception ex)
                {
                    // Un driver defectuoso no debe impedir que se enumeren las demás fuentes.
                    diagnostics.Add($"DirectShow [{device.Name}]: {ex.Message}");
                }
                finally
                {
                    // DirectShow usa COM; liberamos la referencia después de copiar las propiedades necesarias.
                    ReleaseComObject(device);
                }
            }
        }
        catch (Exception ex)
        {
            // Guardamos la excepción para mostrar al técnico la causa real de una lista vacía.
            diagnostics.Add($"DirectShow: {ex.Message}");
        }

        LastEnumerationDiagnostic = cameras.Count > 0
            ? $"DirectShow encontró {cameras.Count} fuente(s) de vídeo local(es)."
            : diagnostics.Count == 0
                ? "Windows no devolvió fuentes de captura de vídeo locales."
                : string.Join(" | ", diagnostics);

        return cameras
            .OrderBy(camera => camera.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<bool> StartAsync(LocalCameraDevice camera, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(camera);
        cancellationToken.ThrowIfCancellationRequested();

        // Cerramos la fuente anterior antes de abrir una nueva para evitar que el driver quede bloqueado.
        Stop();

        var backends = new[]
        {
            VideoCaptureAPIs.MSMF,
            VideoCaptureAPIs.DSHOW,
            VideoCaptureAPIs.ANY
        };

        foreach (var backend in backends)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VideoCapture? candidate = null;

            try
            {
                // candidate abre el índice de captura asociado a esta fuente local.
                candidate = new VideoCapture(camera.CaptureIndex, backend);

                if (!candidate.IsOpened())
                {
                    candidate.Dispose();
                    continue;
                }

                // firstFrame confirma que el dispositivo entrega imágenes reales, no solo que OpenCV pudo abrirlo.
                using var firstFrame = new Mat();
                if (!candidate.Read(firstFrame) || firstFrame.Empty())
                {
                    candidate.Dispose();
                    continue;
                }

                _capture = candidate;
                _captureWidth = firstFrame.Width;
                _captureHeight = firstFrame.Height;
                _captureFps = NormalizeFps(candidate.Fps);

                // _latestFrame conserva una copia independiente del frame para snapshot y grabación.
                lock (_sync)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = firstFrame.Clone();
                }

                // Publicamos el primer frame inmediatamente para que la UI muestre imagen sin esperar otro ciclo.
                PublishFrame(firstFrame);

                _captureCts = new CancellationTokenSource();
                _captureTask = Task.Run(() => CaptureLoop(_captureCts.Token), CancellationToken.None);

                // GetBackendName devuelve el backend que realmente consiguió abrir la cámara.
                LastCaptureDiagnostic =
                    $"STREAM ACTIVO · Backend: {candidate.GetBackendName()} · " +
                    $"Resolución: {_captureWidth}x{_captureHeight} · FPS: {_captureFps:0.##}.";

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                LastCaptureDiagnostic = $"No se pudo abrir '{camera.Name}' con {backend}: {ex.Message}";
                candidate?.Dispose();
            }
        }

        LastCaptureDiagnostic =
            $"No se pudo obtener ningún frame de '{camera.Name}' mediante Media Foundation/DirectShow. " +
            "Verifique driver, privacidad de cámara y que otra aplicación no esté utilizando el dispositivo.";

        return Task.FromResult(false);
    }

    private async Task CaptureLoop(CancellationToken cancellationToken)
    {
        using var frame = new Mat();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var capture = _capture;
                if (capture is null)
                    break;

                // Read obtiene el siguiente frame entregado por el backend nativo de Windows.
                if (!capture.Read(frame) || frame.Empty())
                {
                    await Task.Delay(20, cancellationToken);
                    continue;
                }

                lock (_sync)
                {
                    // Reemplazamos la copia anterior para que snapshot siempre utilice el frame más reciente.
                    _latestFrame?.Dispose();
                    _latestFrame = frame.Clone();

                    // Solo existe writer durante una grabación iniciada explícitamente por el usuario.
                    _videoWriter?.Write(frame);
                }

                // Convertimos el frame de OpenCV a BGRA para WPF.
                PublishFrame(frame);
                await Task.Delay(1, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Detención normal solicitada por Stop().
        }
        catch (Exception ex)
        {
            LastCaptureDiagnostic = $"La captura se detuvo por un error: {ex.Message}";
        }
    }

    private void PublishFrame(Mat bgrFrame)
    {
        if (bgrFrame.Empty())
            return;

        using var bgra = new Mat();

        if (bgrFrame.Channels() == 1)
            Cv2.CvtColor(bgrFrame, bgra, ColorConversionCodes.GRAY2BGRA);
        else if (bgrFrame.Channels() == 4)
            bgrFrame.CopyTo(bgra);
        else
            Cv2.CvtColor(bgrFrame, bgra, ColorConversionCodes.BGR2BGRA);

        // stride es la cantidad de bytes de una fila BGRA32 completa.
        var stride = checked(bgra.Width * 4);
        // byteCount es el tamaño total del buffer que recibirá WPF.
        var byteCount = checked(stride * bgra.Height);
        var pixels = new byte[byteCount];

        // Copiamos desde memoria nativa OpenCV a memoria administrada sin exponer Mat fuera de Video.
        Marshal.Copy(bgra.Data, pixels, 0, byteCount);

        FrameReady?.Invoke(this, new LocalCameraFrame
        {
            Pixels = pixels,
            Width = bgra.Width,
            Height = bgra.Height,
            Stride = stride,
            CapturedAtUtc = DateTimeOffset.UtcNow
        });
    }

    public bool TakeSnapshot(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Mat? snapshot = null;
        try
        {
            lock (_sync)
            {
                // Clonamos el frame para poder escribirlo fuera del lock y no detener la captura innecesariamente.
                if (_latestFrame is null || _latestFrame.Empty())
                    return false;

                snapshot = _latestFrame.Clone();
            }

            // PNG conserva el frame sin pérdida y no requiere un encoder multimedia adicional.
            return Cv2.ImWrite(filePath, snapshot);
        }
        catch (Exception ex)
        {
            LastCaptureDiagnostic = $"No se pudo guardar el snapshot: {ex.Message}";
            return false;
        }
        finally
        {
            snapshot?.Dispose();
        }
    }

    public bool StartRecording(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        lock (_sync)
        {
            if (_latestFrame is null || _latestFrame.Empty())
            {
                LastCaptureDiagnostic = "No se puede grabar porque todavía no existe un frame válido.";
                return false;
            }

            try
            {
                StopRecordingUnsafe();

                // MJPG en AVI prioriza compatibilidad local y evita requerir un codec H.264 específico del sistema.
                var writer = new VideoWriter(
                    filePath,
                    VideoCaptureAPIs.ANY,
                    FourCC.MJPG,
                    NormalizeFps(_captureFps),
                    new Size(_captureWidth, _captureHeight),
                    true);

                if (!writer.IsOpened())
                {
                    writer.Dispose();
                    LastCaptureDiagnostic = "OpenCV no pudo inicializar el grabador MJPG/AVI.";
                    return false;
                }

                _videoWriter = writer;
                LastCaptureDiagnostic = $"GRABACIÓN ACTIVA · {filePath}";
                return true;
            }
            catch (Exception ex)
            {
                LastCaptureDiagnostic = $"No se pudo iniciar la grabación: {ex.Message}";
                return false;
            }
        }
    }

    public void StopRecording()
    {
        lock (_sync)
        {
            StopRecordingUnsafe();
        }

        LastCaptureDiagnostic = "Grabación detenida.";
    }

    private void StopRecordingUnsafe()
    {
        // El writer solo se crea/libera bajo _sync mientras CaptureLoop escribe los frames.
        _videoWriter?.Release();
        _videoWriter?.Dispose();
        _videoWriter = null;
    }

    public void Stop()
    {
        StopRecording();

        var cts = _captureCts;
        _captureCts = null;
        cts?.Cancel();

        var capture = _capture;
        _capture = null;

        capture?.Release();
        capture?.Dispose();
        cts?.Dispose();

        lock (_sync)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }

        _captureTask = null;
        LastCaptureDiagnostic = "Captura local detenida.";
        FrameReady?.Invoke(this, new LocalCameraFrame
        {
            Pixels = Array.Empty<byte>(),
            Width = 0,
            Height = 0,
            Stride = 0,
            CapturedAtUtc = DateTimeOffset.UtcNow
        });
    }

    public void Dispose()
    {
        Stop();
    }

    private static double NormalizeFps(double fps)
    {
        // Algunos drivers devuelven 0, NaN o cifras fuera de rango; 30 FPS es un valor razonable para el grabador.
        return double.IsFinite(fps) && fps is >= 1 and <= 120 ? fps : 30;
    }

    private static string? ExtractUsbId(string? devicePath, string token)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            return null;

        // match obtiene los cuatro caracteres hexadecimales posteriores a VID_ o PID_.
        var match = Regex.Match(
            devicePath,
            $@"{Regex.Escape(token)}([0-9a-fA-F]{{4}})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

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
            // Algunos drivers no estándar pueden fallar durante la liberación COM; no bloqueamos la enumeración.
        }
    }
}
