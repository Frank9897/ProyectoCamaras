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
/// Enumeración y captura de cámaras locales de Windows.
/// Prioriza DirectShow para mantener compatibilidad con LibVLC y utiliza PnP como respaldo
/// para que un dispositivo visible en Windows no desaparezca silenciosamente de Camera Inspector.
/// </summary>
public sealed class LocalCameraService : ILocalCameraService
{
    private readonly LibVLC _libVlc;
    private Media? _currentMedia;
    private MediaPlayer? _currentPlayer;

    /// <summary>Notifica a la UI qué MediaPlayer debe mostrar o retirar.</summary>
    public event EventHandler<MediaPlayer?>? PlayerChanged;

    /// <summary>Reproductor local actualmente activo.</summary>
    public MediaPlayer? Player => _currentPlayer;

    /// <summary>Diagnóstico de la última enumeración local ejecutada.</summary>
    public string LastEnumerationDiagnostic { get; private set; } = "Todavía no se ejecutó una enumeración local.";

    public LocalCameraService()
    {
        // Inicializamos el motor multimedia local para capturas DirectShow.
        global::LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC("--quiet", "--live-caching=100");
    }

    public IReadOnlyList<LocalCameraDevice> GetAvailableCameras()
    {
        var cameras = new List<LocalCameraDevice>();
        var diagnostics = new List<string>();

        try
        {
            // devices contiene las fuentes de vídeo registradas en la categoría VideoInputDevice de DirectShow.
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            foreach (var device in devices)
            {
                try
                {
                    // name es el nombre amigable utilizado por Windows para la fuente de vídeo.
                    var name = device.Name;
                    // devicePath conserva la identidad física cuando el driver la proporciona.
                    var devicePath = device.DevicePath;
                    // monikerString queda disponible como dato técnico de diagnóstico.
                    var monikerString = device.Mon?.ToString();

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // transport resume si el identificador contiene una referencia USB reconocible.
                    var transport = string.IsNullOrWhiteSpace(devicePath)
                        ? "Local/Virtual"
                        : devicePath.Contains("usb#vid_", StringComparison.OrdinalIgnoreCase)
                            ? "USB"
                            : "Local/Virtual";

                    // usbVendorId identifica el fabricante USB cuando el driver expone VID_.
                    var usbVendorId = ExtractUsbId(devicePath, "vid_");
                    // usbProductId identifica el producto USB cuando el driver expone PID_.
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
                    // Un dispositivo defectuoso no debe impedir continuar con los demás.
                    diagnostics.Add($"DirectShow [{device.Name}]: {ex.Message}");
                }
                finally
                {
                    // Liberamos la referencia COM del dispositivo después de copiar sus propiedades.
                    ReleaseComObject(device);
                }
            }
        }
        catch (Exception ex)
        {
            // Guardamos la excepción en vez de ocultarla para facilitar el diagnóstico en equipos de trabajo.
            diagnostics.Add($"DirectShow: {ex.Message}");
        }

        // PnP funciona como respaldo cuando DirectShow no expone ninguna fuente de vídeo.
        if (cameras.Count == 0)
        {
            var pnpCameras = GetPnpCameraDevices(out var pnpDiagnostic);

            foreach (var pnpCamera in pnpCameras)
            {
                // Evitamos duplicados por nombre cuando ambos enumeradores conocen el mismo dispositivo.
                if (cameras.Any(item => string.Equals(item.Name, pnpCamera.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;

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
            // startInfo consulta únicamente dispositivos PnP presentes y nunca modifica el equipo.
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // ArgumentList evita problemas de escapado causados por nombres o rutas externas.
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(
                "Get-PnpDevice -PresentOnly | " +
                "Where-Object { $_.Status -eq 'OK' -and ($_.Class -eq 'Camera' -or $_.Class -eq 'Image' -or $_.Service -eq 'usbvideo') } | " +
                "Select-Object FriendlyName,InstanceId,Class,Status,Service | " +
                "ConvertTo-Json -Compress");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                diagnostic = "PnP: no se pudo iniciar PowerShell.";
                return result;
            }

            // output contiene el resultado serializado de la consulta de dispositivos.
            var output = process.StandardOutput.ReadToEnd();
            // error contiene cualquier mensaje de error emitido por PowerShell.
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

            // instanceId identifica el dispositivo PnP aunque no tenga un moniker DirectShow disponible.
            var instanceId = device.InstanceId;
            // isUsb permite informar al técnico de que Windows lo presenta como dispositivo USB.
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

    public bool Play(LocalCameraDevice camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        // Una única captura local activa evita conflictos con drivers que no permiten uso compartido.
        Stop();

        if (!camera.PreviewSupported)
            return false;

        // dshow:// es el origen de captura DirectShow de VLC en Windows.
        _currentMedia = new Media(_libVlc, "dshow://", FromType.FromLocation);
        // dshow-vdev selecciona la fuente por FriendlyName registrado en DirectShow.
        _currentMedia.AddOption($":dshow-vdev={EscapeOption(camera.Name)}");
        // No abrimos audio automáticamente porque el objetivo actual es vídeo.
        _currentMedia.AddOption(":dshow-adev=none");
        // live-caching pequeño para mantener baja la latencia.
        _currentMedia.AddOption(":live-caching=100");

        _currentPlayer = new MediaPlayer(_libVlc);

        // started indica si LibVLC aceptó la apertura de la fuente de vídeo.
        var started = _currentPlayer.Play(_currentMedia);
        if (!started)
        {
            Stop();
            return false;
        }

        PlayerChanged?.Invoke(this, _currentPlayer);
        return true;
    }

    public void Stop()
    {
        if (_currentPlayer is not null)
        {
            // IsPlaying indica si la captura local continúa activa.
            if (_currentPlayer.IsPlaying)
                _currentPlayer.Stop();

            _currentPlayer.Dispose();
            _currentPlayer = null;
        }

        _currentMedia?.Dispose();
        _currentMedia = null;

        PlayerChanged?.Invoke(this, null);
    }

    public void Dispose()
    {
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
            // Algunos drivers no liberan correctamente COM; no propagamos ese fallo a la aplicación.
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
