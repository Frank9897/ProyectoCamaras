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
    private readonly object _sync = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private Mat? _latestFrame;
    private VideoWriter? _videoWriter;

    private double _captureFps = 30;
    private double _effectiveCaptureFps = 30;
    private int _captureWidth;
    private int _captureHeight;
    private string _captureBackendName = "DESCONOCIDO";
    private bool _capturePreferMjpeg;
    private long _framesCaptured;
    private DateTimeOffset _lastFrameAtUtc = DateTimeOffset.MinValue;

    private const int PreviewTargetFps = 20;
    private const int PreviewMaxWidth = 960;

    public event EventHandler<LocalCameraFrame>? FrameReady;

    public string LastEnumerationDiagnostic { get; private set; } = "Todavía no se ejecutó una enumeración local.";
    public string LastCaptureDiagnostic { get; private set; } = "Todavía no se abrió una cámara local.";
    public bool IsCapturing => _capture is not null;

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
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            for (var index = 0; index < devices.Length; index++)
            {
                var device = devices[index];
                try
                {
                    var name = device.Name;
                    var devicePath = device.DevicePath;
                    var monikerString = device.Mon?.ToString();

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var transport = string.IsNullOrWhiteSpace(devicePath)
                        ? "Local/Virtual"
                        : devicePath.Contains("usb#vid_", StringComparison.OrdinalIgnoreCase)
                            ? "USB"
                            : "Local/Virtual";

                    cameras.Add(new LocalCameraDevice
                    {
                        Name = name,
                        DevicePath = devicePath,
                        MonikerString = monikerString,
                        DiscoverySource = "DirectShow",
                        PreviewSupported = true,
                        CaptureIndex = index,
                        Transport = transport,
                        UsbVendorId = ExtractUsbId(devicePath, "vid_"),
                        UsbProductId = ExtractUsbId(devicePath, "pid_"),
                        IsVideoCaptureDevice = true,
                        Status = "Disponible"
                    });
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"DirectShow [{device.Name}]: {ex.Message}");
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add($"DirectShow: {ex.Message}");
        }

        LastEnumerationDiagnostic = cameras.Count > 0
            ? $"DirectShow encontró {cameras.Count} fuente(s) de vídeo local(es)."
            : diagnostics.Count == 0
                ? "Windows no devolvió fuentes de captura de vídeo locales."
                : string.Join(" | ", diagnostics);

        return cameras.OrderBy(camera => camera.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Construye opciones de grabación sin abrir/cerrar repetidamente la webcam.
    /// Se parte del modo actualmente negociado y se agregan resoluciones de trabajo habituales.
    /// La compatibilidad final se valida únicamente al aplicar la opción elegida.
    /// </summary>
    public IReadOnlyList<LocalCameraCapability> GetCapabilities(LocalCameraDevice camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        var capabilities = new List<LocalCameraCapability>();
        var backend = string.IsNullOrWhiteSpace(_captureBackendName) || _captureBackendName == "DESCONOCIDO"
            ? "DSHOW"
            : _captureBackendName;
        var format = _capturePreferMjpeg ? "MJPG" : "Nativo";
        var currentFps = NormalizeFps(_effectiveCaptureFps);

        var candidates = new (int Width, int Height)[]
        {
            (_captureWidth, _captureHeight),
            (1920, 1080),
            (1280, 720),
            (1280, 1024),
            (1024, 768),
            (800, 600),
            (640, 480),
            (320, 240)
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (width, height) in candidates)
        {
            if (width <= 0 || height <= 0)
                continue;

            var key = $"{width}x{height}|{backend}|{format}";
            if (!seen.Add(key))
                continue;

            capabilities.Add(new LocalCameraCapability
            {
                Width = width,
                Height = height,
                Fps = currentFps,
                Backend = backend,
                Format = format
            });
        }

        return capabilities
            .OrderByDescending(item => item.Width * (long)item.Height)
            .ThenBy(item => item.Width)
            .ToList();
    }

    public Task<bool> StartAsync(LocalCameraDevice camera, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(camera);
        cancellationToken.ThrowIfCancellationRequested();
        Stop();

        var attempts = new[]
        {
            new CaptureAttempt(VideoCaptureAPIs.DSHOW, 1920, 1080, true),
            new CaptureAttempt(VideoCaptureAPIs.DSHOW, 1280, 720, true),
            new CaptureAttempt(VideoCaptureAPIs.DSHOW, 640, 480, true),
            new CaptureAttempt(VideoCaptureAPIs.DSHOW, 1920, 1080, false),
            new CaptureAttempt(VideoCaptureAPIs.DSHOW, 1280, 720, false),
            new CaptureAttempt(VideoCaptureAPIs.DSHOW, 640, 480, false),
            new CaptureAttempt(VideoCaptureAPIs.MSMF, 1920, 1080, false),
            new CaptureAttempt(VideoCaptureAPIs.MSMF, 1280, 720, false),
            new CaptureAttempt(VideoCaptureAPIs.MSMF, 640, 480, false),
            new CaptureAttempt(VideoCaptureAPIs.ANY, 0, 0, false)
        };

        var diagnostics = new List<string>();

        foreach (var attempt in attempts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (TryOpenCapture(camera, attempt.Backend, attempt.Width, attempt.Height, attempt.PreferMjpeg, cancellationToken, out var candidate, out var firstFrame))
                {
                    ActivateCapture(candidate!, firstFrame!, attempt.PreferMjpeg);
                    return Task.FromResult(true);
                }

                diagnostics.Add($"{attempt.Description}: no entregó frames.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                diagnostics.Add($"{attempt.Description}: {ex.Message}");
            }
        }

        LastCaptureDiagnostic =
            $"La cámara '{camera.Name}' fue detectada, pero ningún modo entregó frames. " +
            string.Join(" | ", diagnostics);

        return Task.FromResult(false);
    }

    public Task<bool> StartAsync(LocalCameraDevice camera, LocalCameraCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(capability);
        cancellationToken.ThrowIfCancellationRequested();
        Stop();

        var backend = ParseBackend(capability.Backend);
        var preferMjpeg = string.Equals(capability.Format, "MJPG", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (!TryOpenCapture(camera, backend, capability.Width, capability.Height, preferMjpeg, cancellationToken, out var candidate, out var firstFrame))
            {
                LastCaptureDiagnostic =
                    $"No se pudo abrir {capability.Width}x{capability.Height} {capability.Format} " +
                    $"con {capability.Backend}.";
                return Task.FromResult(false);
            }

            ActivateCapture(candidate!, firstFrame!, preferMjpeg);
            return Task.FromResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastCaptureDiagnostic = $"No se pudo abrir el modo seleccionado: {ex.Message}";
            return Task.FromResult(false);
        }
    }

    private bool TryOpenCapture(
        LocalCameraDevice camera,
        VideoCaptureAPIs backend,
        int width,
        int height,
        bool preferMjpeg,
        CancellationToken cancellationToken,
        out VideoCapture? candidate,
        out Mat? firstFrame)
    {
        candidate = null;
        firstFrame = null;

        candidate = new VideoCapture(camera.CaptureIndex, backend);
        if (!candidate.IsOpened())
        {
            candidate.Dispose();
            candidate = null;
            return false;
        }

        if (preferMjpeg)
            candidate.Set(VideoCaptureProperties.FourCC, FourCC.MJPG);
        if (width > 0)
            candidate.Set(VideoCaptureProperties.FrameWidth, width);
        if (height > 0)
            candidate.Set(VideoCaptureProperties.FrameHeight, height);

        candidate.Set(VideoCaptureProperties.Fps, 30);
        Thread.Sleep(120);

        firstFrame = new Mat();
        var received = false;

        for (var i = 0; i < 8 && !received; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Read(firstFrame) && !firstFrame.Empty())
                received = true;
            else
                Thread.Sleep(40);
        }

        if (!received)
        {
            firstFrame.Dispose();
            firstFrame = null;
            candidate.Release();
            candidate.Dispose();
            candidate = null;
            return false;
        }

        return true;
    }

    private void ActivateCapture(VideoCapture candidate, Mat firstFrame, bool preferMjpeg)
    {
        _capture = candidate;
        _captureWidth = firstFrame.Width;
        _captureHeight = firstFrame.Height;
        _captureFps = NormalizeFps(candidate.Fps);
        _effectiveCaptureFps = _captureFps;
        _captureBackendName = candidate.GetBackendName();
        _capturePreferMjpeg = preferMjpeg;
        _framesCaptured = 1;
        _lastFrameAtUtc = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            _latestFrame?.Dispose();
            _latestFrame = firstFrame.Clone();
        }

        PublishFrame(firstFrame);
        firstFrame.Dispose();

        _captureCts = new CancellationTokenSource();
        _captureTask = Task.Run(() => CaptureLoop(_captureCts.Token), CancellationToken.None);

        LastCaptureDiagnostic =
            $"STREAM ACTIVO · Backend: {_captureBackendName} · " +
            $"Resolución: {_captureWidth}x{_captureHeight} · FPS declarado: {_captureFps:0.##} · " +
            $"FPS efectivo: {_effectiveCaptureFps:0.##} · Preview: máximo {PreviewTargetFps} FPS / {PreviewMaxWidth}px.";
    }

    private async Task CaptureLoop(CancellationToken cancellationToken)
    {
        using var frame = new Mat();
        var previewClock = Stopwatch.StartNew();
        var fpsClock = Stopwatch.StartNew();
        var lastPreviewMilliseconds = -1_000L;
        var previewIntervalMilliseconds = Math.Max(1, 1000 / PreviewTargetFps);
        var framesAtLastFpsSample = 1L;
        var lastFpsSampleMilliseconds = 0L;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var capture = _capture;
                if (capture is null)
                    break;

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
                    _latestFrame?.Dispose();
                    _latestFrame = frame.Clone();
                    _videoWriter?.Write(frame);
                }

                _framesCaptured++;
                _lastFrameAtUtc = DateTimeOffset.UtcNow;

                var fpsNow = fpsClock.ElapsedMilliseconds;
                if (_framesCaptured - framesAtLastFpsSample >= 30)
                {
                    var elapsedMilliseconds = fpsNow - lastFpsSampleMilliseconds;
                    if (elapsedMilliseconds >= 250)
                    {
                        var observedFps = (_framesCaptured - framesAtLastFpsSample) / (elapsedMilliseconds / 1000d);
                        if (double.IsFinite(observedFps) && observedFps is >= 5 and <= 120)
                        {
                            _effectiveCaptureFps = _effectiveCaptureFps * 0.35 + observedFps * 0.65;
                            LastCaptureDiagnostic =
                                $"STREAM ACTIVO · Backend: {_captureBackendName} · " +
                                $"Resolución: {_captureWidth}x{_captureHeight} · FPS efectivo: {_effectiveCaptureFps:0.##} · " +
                                $"Preview: máximo {PreviewTargetFps} FPS / {PreviewMaxWidth}px.";
                        }

                        framesAtLastFpsSample = _framesCaptured;
                        lastFpsSampleMilliseconds = fpsNow;
                    }
                }

                var nowMilliseconds = previewClock.ElapsedMilliseconds;
                if (nowMilliseconds - lastPreviewMilliseconds >= previewIntervalMilliseconds)
                {
                    lastPreviewMilliseconds = nowMilliseconds;
                    PublishFrame(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LastCaptureDiagnostic = $"La captura se detuvo por un error: {ex.Message}";
        }
        finally
        {
            previewClock.Stop();
            fpsClock.Stop();
        }
    }

    private void PublishFrame(Mat bgrFrame)
    {
        if (bgrFrame.Empty())
            return;

        using var previewFrame = PreparePreviewFrame(bgrFrame);
        using var bgra = new Mat();

        if (previewFrame.Channels() == 1)
            Cv2.CvtColor(previewFrame, bgra, ColorConversionCodes.GRAY2BGRA);
        else if (previewFrame.Channels() == 4)
            previewFrame.CopyTo(bgra);
        else
            Cv2.CvtColor(previewFrame, bgra, ColorConversionCodes.BGR2BGRA);

        var stride = checked(bgra.Width * 4);
        var byteCount = checked(stride * bgra.Height);
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
        var sourceWidth = source.Width;
        var sourceHeight = source.Height;

        if (sourceWidth <= PreviewMaxWidth)
            return source.Clone();

        var scale = PreviewMaxWidth / (double)sourceWidth;
        var previewHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var resized = new Mat();
        Cv2.Resize(source, resized, new Size(PreviewMaxWidth, previewHeight), 0, 0, InterpolationFlags.Area);
        return resized;
    }

    private bool TryProbeCapability(
        LocalCameraDevice camera,
        VideoCaptureAPIs backend,
        int width,
        int height,
        bool preferMjpeg,
        out int actualWidth,
        out int actualHeight,
        out double fps)
    {
        actualWidth = 0;
        actualHeight = 0;
        fps = 0;

        VideoCapture? capture = null;
        try
        {
            capture = new VideoCapture(camera.CaptureIndex, backend);
            if (!capture.IsOpened())
                return false;

            if (preferMjpeg)
                capture.Set(VideoCaptureProperties.FourCC, FourCC.MJPG);
            capture.Set(VideoCaptureProperties.FrameWidth, width);
            capture.Set(VideoCaptureProperties.FrameHeight, height);
            capture.Set(VideoCaptureProperties.Fps, 30);
            Thread.Sleep(80);

            using var frame = new Mat();
            for (var i = 0; i < 4; i++)
            {
                if (!capture.Read(frame) || frame.Empty())
                    continue;

                actualWidth = frame.Width;
                actualHeight = frame.Height;
                fps = NormalizeFps(capture.Fps);
                return actualWidth > 0 && actualHeight > 0;
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            capture?.Release();
            capture?.Dispose();
        }
    }

    public bool TakeSnapshot(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Mat? snapshot = null;
        try
        {
            lock (_sync)
            {
                if (_latestFrame is null || _latestFrame.Empty())
                    return false;
                snapshot = _latestFrame.Clone();
            }

            var extension = Path.GetExtension(filePath);
            var parameters = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                             extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? new[] { (int)ImwriteFlags.JpegQuality, 95 }
                : Array.Empty<int>();

            return parameters.Length == 0
                ? Cv2.ImWrite(filePath, snapshot)
                : Cv2.ImWrite(filePath, snapshot, parameters);
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

    public bool StartRecording(string filePath, LocalCameraCapability? capability = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!_captureIsCompatible(capability))
        {
            if (_capture is null || _selectedCameraForReconfigure is null)
            {
                LastCaptureDiagnostic = "No se puede cambiar la resolución de grabación sin una cámara activa.";
                return false;
            }

            try
            {
                var camera = _selectedCameraForReconfigure;
                var restarted = StartAsync(camera, capability!).GetAwaiter().GetResult();
                if (!restarted)
                    return false;
            }
            catch (Exception ex)
            {
                LastCaptureDiagnostic = $"No se pudo aplicar la resolución seleccionada: {ex.Message}";
                return false;
            }
        }

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

                var recordingFps = NormalizeFps(_effectiveCaptureFps);
                var writer = new VideoWriter(
                    filePath,
                    VideoCaptureAPIs.ANY,
                    FourCC.MJPG,
                    recordingFps,
                    new Size(_captureWidth, _captureHeight),
                    true);

                if (!writer.IsOpened())
                {
                    writer.Dispose();
                    LastCaptureDiagnostic = "OpenCV no pudo inicializar el grabador MJPG/AVI.";
                    return false;
                }

                _videoWriter = writer;
                LastCaptureDiagnostic =
                    $"GRABACIÓN ACTIVA · {filePath} · {_captureWidth}x{_captureHeight} · {recordingFps:0.##} FPS.";
                return true;
            }
            catch (Exception ex)
            {
                LastCaptureDiagnostic = $"No se pudo iniciar la grabación: {ex.Message}";
                return false;
            }
        }
    }

    private bool _captureIsCompatible(LocalCameraCapability? capability)
    {
        if (capability is null)
            return true;

        return _capture is not null &&
               _captureWidth == capability.Width &&
               _captureHeight == capability.Height &&
               string.Equals(_captureBackendName, capability.Backend, StringComparison.OrdinalIgnoreCase) &&
               _capturePreferMjpeg == string.Equals(capability.Format, "MJPG", StringComparison.OrdinalIgnoreCase);
    }

    private LocalCameraDevice? _selectedCameraForReconfigure;

    public void SetActiveCamera(LocalCameraDevice camera)
    {
        _selectedCameraForReconfigure = camera;
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
        _effectiveCaptureFps = 30;
        _lastFrameAtUtc = DateTimeOffset.MinValue;
        _captureTask = null;
        _captureWidth = 0;
        _captureHeight = 0;
        _captureBackendName = "DESCONOCIDO";
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

    private static VideoCaptureAPIs ParseBackend(string backend)
    {
        if (backend.Contains("MSMF", StringComparison.OrdinalIgnoreCase))
            return VideoCaptureAPIs.MSMF;
        if (backend.Contains("DSHOW", StringComparison.OrdinalIgnoreCase) || backend.Contains("DirectShow", StringComparison.OrdinalIgnoreCase))
            return VideoCaptureAPIs.DSHOW;
        return VideoCaptureAPIs.ANY;
    }

    private static double NormalizeFps(double fps)
    {
        return double.IsFinite(fps) && fps is >= 1 and <= 120 ? fps : 30;
    }

    private static string? ExtractUsbId(string? devicePath, string token)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            return null;

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
        }
    }

    private readonly record struct CaptureAttempt(
        VideoCaptureAPIs Backend,
        int Width,
        int Height,
        bool PreferMjpeg)
    {
        public string Description =>
            $"{Backend} {(Width > 0 && Height > 0 ? $"{Width}x{Height}" : "predeterminada")} " +
            $"{(PreferMjpeg ? "MJPG" : "nativa")}";
    }
}
