using System.Diagnostics;
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
/// priorizando DirectShow para webcams UVC y utilizando Media Foundation como respaldo.
/// </summary>
public sealed class LocalCameraService : ILocalCameraService
{
    // _sync protege el último frame y el grabador cuando el hilo de captura y la UI acceden simultáneamente.
    private readonly object _sync = new();
    // _capture mantiene abierto el dispositivo de vídeo seleccionado.
    private VideoCapture? _capture;
    // _captureCts permite detener limpiamente el bucle de captura.
    private CancellationTokenSource? _captureCts;
    // _captureTask representa el trabajo de lectura de frames en segundo plano.
    private Task? _captureTask;
    // _latestFrame conserva una copia reciente para snapshot y para la grabación.
    private Mat? _latestFrame;
    // _videoWriter solo existe mientras el usuario mantiene activa una grabación.
    private VideoWriter? _videoWriter;
    // _captureFps guarda la frecuencia que el driver expone para el stream.
    private double _captureFps = 30;
    // _captureWidth contiene el ancho nativo del stream.
    private int _captureWidth;
    // _captureHeight contiene el alto nativo del stream.
    private int _captureHeight;
    // _framesCaptured acumula la cantidad de frames realmente recibidos del dispositivo.
    private long _framesCaptured;
    // _lastFrameAtUtc identifica cuándo llegó el último frame válido.
    private DateTimeOffset _lastFrameAtUtc = DateTimeOffset.MinValue;

    // PreviewTargetFps limita las actualizaciones visuales; la cámara puede seguir capturando a su FPS nativo.
    private const int PreviewTargetFps = 20;
    // PreviewMaxWidth evita convertir imágenes gigantes cuando solo necesitamos una vista previa técnica.
    private const int PreviewMaxWidth = 960;

    /// <summary>Emite frames BGRA destinados a la vista previa WPF.</summary>
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
            // DirectShow proporciona nombres, DevicePath y una enumeración útil para mapear el dispositivo al índice de OpenCV.
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            for (var index = 0; index < devices.Length; index++)
            {
                var device = devices[index];
                try
                {
                    // name es el nombre amigable que Windows muestra al usuario.
                    var name = device.Name;
                    // devicePath conserva la identidad técnica para diagnóstico y matching futuro.
                    var devicePath = device.DevicePath;
                    // monikerString conserva el identificador COM de DirectShow.
                    var monikerString = device.Mon?.ToString();

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // transport clasifica el dispositivo como USB o Local/Virtual según su DevicePath.
                    var transport = string.IsNullOrWhiteSpace(devicePath)
                        ? "Local/Virtual"
                        : devicePath.Contains("usb#vid_", StringComparison.OrdinalIgnoreCase)
                            ? "USB"
                            : "Local/Virtual";

                    // usbVendorId identifica el fabricante USB a partir de VID_.
                    var usbVendorId = ExtractUsbId(devicePath, "vid_");
                    // usbProductId identifica el producto USB a partir de PID_.
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
                    // Un controlador defectuoso no debe impedir enumerar los dispositivos restantes.
                    diagnostics.Add($"DirectShow [{device.Name}]: {ex.Message}");
                }
                finally
                {
                    // Liberamos el objeto COM una vez copiadas todas las propiedades necesarias.
                    ReleaseComObject(device);
                }
            }
        }
        catch (Exception ex)
        {
            // Guardamos la excepción para explicar al técnico por qué la lista puede estar vacía.
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

        // Cerramos cualquier captura anterior antes de abrir otra para que el driver no quede ocupado.
        Stop();

        // UVC suele comportarse mejor con DirectShow y MJPG en webcams que exponen varios modos comprimidos.
        var attempts = new[]
        {
            new CaptureAttempt(VideoCaptureAPIs.DSHOW, 1280, 720, true),
            new CaptureAttempt(VideoCaptureAPIs.DSHOW, 640, 480, true),
            new CaptureAttempt(VideoCaptureAPIs.DSHOW, 1280, 720, false),
            new CaptureAttempt(VideoCaptureAPIs.DSHOW, 640, 480, false),
            new CaptureAttempt(VideoCaptureAPIs.MSMF, 1280, 720, false),
            new CaptureAttempt(VideoCaptureAPIs.MSMF, 640, 480, false),
            new CaptureAttempt(VideoCaptureAPIs.ANY, 0, 0, false)
        };

        var diagnostics = new List<string>();

        foreach (var attempt in attempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VideoCapture? candidate = null;

            try
            {
                // candidate abre el índice de captura asociado a esta fuente local.
                candidate = new VideoCapture(camera.CaptureIndex, attempt.Backend);
                if (!candidate.IsOpened())
                {
                    diagnostics.Add($"{attempt.Description}: no abrió el dispositivo.");
                    candidate.Dispose();
                    continue;
                }

                // FOURCC.MJPG pide al driver un modo MJPEG cuando la webcam lo soporta.
                if (attempt.PreferMjpeg)
                    candidate.Set(VideoCaptureProperties.FourCC, FourCC.MJPG);

                // FrameWidth y FrameHeight solicitan la resolución deseada, pero el driver puede elegir otra compatible.
                if (attempt.Width > 0)
                    candidate.Set(VideoCaptureProperties.FrameWidth, attempt.Width);
                if (attempt.Height > 0)
                    candidate.Set(VideoCaptureProperties.FrameHeight, attempt.Height);
                candidate.Set(VideoCaptureProperties.Fps, 30);

                // Algunos drivers necesitan unos milisegundos para comenzar a entregar buffers después de abrirse.
                Thread.Sleep(150);

                using var firstFrame = new Mat();
                var firstFrameReceived = false;

                // Probamos varios Read para evitar descartar un modo que tarda unos ciclos en entregar el primer buffer.
                for (var i = 0; i < 8 && !firstFrameReceived; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (candidate.Read(firstFrame) && !firstFrame.Empty())
                        firstFrameReceived = true;
                    else
                        Thread.Sleep(40);
                }

                if (!firstFrameReceived)
                {
                    diagnostics.Add($"{attempt.Description}: abierto pero no entregó frames.");
                    candidate.Release();
                    candidate.Dispose();
                    continue;
                }

                _capture = candidate;
                _captureWidth = firstFrame.Width;
                _captureHeight = firstFrame.Height;
                _captureFps = NormalizeFps(candidate.Fps);
                _framesCaptured = 1;
                _lastFrameAtUtc = DateTimeOffset.UtcNow;

                lock (_sync)
                {
                    // _latestFrame es una única copia de trabajo, no una cola acumulativa.
                    _latestFrame?.Dispose();
                    _latestFrame = firstFrame.Clone();
                }

                // Publicamos el primer frame inmediatamente para que la UI muestre imagen sin esperar otro ciclo.
                PublishFrame(firstFrame);

                _captureCts = new CancellationTokenSource();
                _captureTask = Task.Run(() => CaptureLoop(_captureCts.Token), CancellationToken.None);

                LastCaptureDiagnostic =
                    $"STREAM ACTIVO · Backend: {candidate.GetBackendName()} · " +
                    $"Resolución: {_captureWidth}x{_captureHeight} · FPS: {_captureFps:0.##} · " +
                    $"Preview: máximo {PreviewTargetFps} FPS / {PreviewMaxWidth}px.";

                return Task.FromResult(true);
            }
            catch (OperationCanceledException)
            {
                candidate?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                diagnostics.Add($"{attempt.Description}: {ex.Message}");
                candidate?.Release();
                candidate?.Dispose();
            }
        }

        LastCaptureDiagnostic =
            $"La cámara '{camera.Name}' fue detectada, pero ningún modo entregó frames. " +
            string.Join(" | ", diagnostics);

        return Task.FromResult(false);
    }

    private async Task CaptureLoop(CancellationToken cancellationToken)
    {
        using var frame = new Mat();
        // previewClock mide el tiempo sin depender de la velocidad real que reporte el driver.
        var previewClock = Stopwatch.StartNew();
        // lastPreviewMilliseconds guarda cuándo publicamos la última imagen a WPF.
        var lastPreviewMilliseconds = -1_000L;
        // previewIntervalMilliseconds determina el intervalo entre actualizaciones visuales.
        var previewIntervalMilliseconds = Math.Max(1, 1000 / PreviewTargetFps);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var capture = _capture;
                if (capture is null)
                    break;

                // Read solicita el siguiente buffer de vídeo al backend de Windows.
                if (!capture.Read(frame) || frame.Empty())
                {
                    LastCaptureDiagnostic =
                        $"STREAM DEGRADADO · Backend: {capture.GetBackendName()} · " +
                        $"Frames recibidos: {_framesCaptured} · sin frame en este ciclo.";
                    await Task.Delay(20, cancellationToken);
                    continue;
                }

                lock (_sync)
                {
                    // Reemplazamos la única copia de snapshot; no acumulamos frames en memoria.
                    _latestFrame?.Dispose();
                    _latestFrame = frame.Clone();

                    // La grabación utiliza el frame nativo completo, no el frame reducido del preview.
                    _videoWriter?.Write(frame);
                }

                _framesCaptured++;
                _lastFrameAtUtc = DateTimeOffset.UtcNow;

                // nowMilliseconds permite descartar trabajo visual innecesario sin frenar la captura.
                var nowMilliseconds = previewClock.ElapsedMilliseconds;
                if (nowMilliseconds - lastPreviewMilliseconds >= previewIntervalMilliseconds)
                {
                    lastPreviewMilliseconds = nowMilliseconds;
                    // PublishFrame reduce la resolución solo para el preview y entrega como máximo PreviewTargetFps.
                    PublishFrame(frame);
                }

                // Read normalmente bloquea esperando el siguiente frame; no necesitamos un retardo artificial adicional.
            }
        }
        catch (OperationCanceledException)
        {
            // La cancelación forma parte del cierre normal de una captura.
        }
        catch (Exception ex)
        {
            LastCaptureDiagnostic = $"La captura se detuvo por un error: {ex.Message}";
        }
        finally
        {
            previewClock.Stop();
        }
    }

    private void PublishFrame(Mat bgrFrame)
    {
        if (bgrFrame.Empty())
            return;

        // previewFrame es una copia reducida solo cuando la cámara entrega una resolución superior al límite visual.
        using var previewFrame = PreparePreviewFrame(bgrFrame);
        using var bgra = new Mat();

        if (previewFrame.Channels() == 1)
            Cv2.CvtColor(previewFrame, bgra, ColorConversionCodes.GRAY2BGRA);
        else if (previewFrame.Channels() == 4)
            previewFrame.CopyTo(bgra);
        else
            Cv2.CvtColor(previewFrame, bgra, ColorConversionCodes.BGR2BGRA);

        // stride indica los bytes de una fila BGRA32 completa.
        var stride = checked(bgra.Width * 4);
        // byteCount indica cuántos bytes se copiarán al buffer administrado.
        var byteCount = checked(stride * bgra.Height);
        // pixels contiene únicamente la imagen que la UI necesita mostrar.
        var pixels = new byte[byteCount];

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

    private static Mat PreparePreviewFrame(Mat source)
    {
        // sourceWidth es el ancho nativo que entregó la webcam.
        var sourceWidth = source.Width;
        // sourceHeight es el alto nativo que entregó la webcam.
        var sourceHeight = source.Height;

        if (sourceWidth <= PreviewMaxWidth)
            return source.Clone();

        // scale mantiene la proporción original al reducir la imagen para la interfaz.
        var scale = PreviewMaxWidth / (double)sourceWidth;
        // previewHeight calcula la altura proporcional al nuevo ancho.
        var previewHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        // resized es el frame optimizado que se convertirá a BGRA.
        var resized = new Mat();

        // INTER_AREA está pensado para reducción y evita procesar más píxeles de los necesarios en WPF.
        Cv2.Resize(source, resized, new Size(PreviewMaxWidth, previewHeight), 0, 0, InterpolationFlags.Area);
        return resized;
    }

    public bool TakeSnapshot(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Mat? snapshot = null;
        try
        {
            lock (_sync)
            {
                // Clonamos el último frame completo para no escribir mientras otro hilo lo reemplaza.
                if (_latestFrame is null || _latestFrame.Empty())
                    return false;

                snapshot = _latestFrame.Clone();
            }

            // El snapshot conserva la resolución nativa del dispositivo, no la resolución reducida del preview.
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

                // MJPG/AVI prioriza compatibilidad local y evita depender de un codec H.264 específico del sistema.
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

        _framesCaptured = 0;
        _lastFrameAtUtc = DateTimeOffset.MinValue;
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
        // Algunos drivers devuelven 0, NaN o valores fuera de rango; 30 FPS es un respaldo razonable.
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
            // Un driver no estándar puede fallar al liberar COM; la enumeración no debe detenerse por ello.
        }
    }

    /// <summary>
    /// Describe un intento concreto de apertura para que el diagnóstico indique backend, resolución y formato solicitado.
    /// </summary>
    private readonly record struct CaptureAttempt(
        VideoCaptureAPIs Backend,
        int Width,
        int Height,
        bool PreferMjpeg)
    {
        // Description es un resumen legible del intento que aparece en el diagnóstico de fallos.
        public string Description =>
            $"{Backend} {(Width > 0 && Height > 0 ? $"{Width}x{Height}" : "predeterminada")} " +
            $"{(PreferMjpeg ? "MJPG" : "nativa")}";
    }
}
