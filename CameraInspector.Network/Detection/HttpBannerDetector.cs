using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detector basado en respuesta HTTP del dispositivo. Acepta tanto banners modernos
/// como páginas HTML de equipos VIVOTEK antiguos sin ONVIF.
/// </summary>
public sealed class HttpBannerDetector : IManufacturerDetector
{
    public string Name => "HttpBanner";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMilliseconds(1200)
    };

    private static readonly (string Needle, string Manufacturer)[] Signatures =
    {
        ("vivotek", "VIVOTEK"),
        ("vivotek ip camera", "VIVOTEK"),
        ("vivotek network camera", "VIVOTEK"),
        ("vvtk", "VIVOTEK"),
        ("hikvision", "Hikvision"),
        ("dahua", "Dahua"),
        ("dvrdvs", "Dahua"),
        ("axis", "Axis"),
        ("uniview", "Uniview"),
        ("reolink", "Reolink")
    };

    private static readonly string[] CameraIndicators =
    {
        "network camera",
        "ip camera",
        "video server",
        "ipcam",
        "vivotek",
        "vvtk",
        "mjpeg",
        "rtsp"
    };

    public async Task<ManufacturerDetectionResult?> TryDetectAsync(
        DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        foreach (var port in GetHttpPortsToTry(device))
        {
            try
            {
                using var response = await Http.GetAsync($"http://{device.IpAddress}:{port}/", cancellationToken);
                var serverHeader = response.Headers.Server?.ToString() ?? string.Empty;
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var haystack = (serverHeader + " " + body).ToLowerInvariant();

                foreach (var (needle, manufacturer) in Signatures)
                {
                    if (!haystack.Contains(needle, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var model = ExtractModel(haystack, manufacturer);
                    return new ManufacturerDetectionResult
                    {
                        DetectorName = Name,
                        Confidence = manufacturer == "VIVOTEK" ? 0.95 : 0.75,
                        Manufacturer = manufacturer,
                        Model = model,
                        HttpSupported = true,
                        HttpPort = port
                    };
                }

                // Un banner de cámara sin firma conocida sigue siendo evidencia válida.
                if (CameraIndicators.Any(indicator => haystack.Contains(indicator, StringComparison.Ordinal)))
                {
                    return new ManufacturerDetectionResult
                    {
                        DetectorName = Name,
                        Confidence = 0.35,
                        HttpSupported = true,
                        HttpPort = port
                    };
                }

                // Respondió HTTP, aunque no parezca cámara: conservar la evidencia débil.
                return new ManufacturerDetectionResult
                {
                    DetectorName = Name,
                    Confidence = 0.15,
                    HttpSupported = true,
                    HttpPort = port
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Probamos el siguiente puerto; una cámara puede usar un HTTP alternativo.
            }
        }

        return null;
    }

    private static IEnumerable<int> GetHttpPortsToTry(DiscoveredDevice device)
    {
        if (device.HttpPort.HasValue)
            yield return device.HttpPort.Value;

        foreach (var port in new[] { 80, 8080, 8081, 8888, 443 })
        {
            if (device.HttpPort != port)
                yield return port;
        }
    }

    private static string? ExtractModel(string text, string manufacturer)
    {
        if (manufacturer != "VIVOTEK")
            return null;

        var knownModels = new[] { "IP7134", "IP7133" };
        return knownModels.FirstOrDefault(model =>
            text.Contains(model, StringComparison.OrdinalIgnoreCase));
    }
}
