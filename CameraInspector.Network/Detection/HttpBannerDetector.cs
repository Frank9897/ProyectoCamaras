using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detector basado en respuesta HTTP del dispositivo.
/// Un servidor web genérico no se considera cámara salvo que exista una firma de cámara explícita.
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
        ("reolink", "Reolink"),
        ("hanwha", "Hanwha"),
        ("wisenet", "Hanwha"),
        ("mobotix", "MOBOTIX")
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
        "rtsp",
        "wisenet",
        "mobotix",
        "axis camera"
    };

    public async Task<ManufacturerDetectionResult?> TryDetectAsync(
        DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        foreach (var port in GetHttpPortsToTry(device))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scheme = port is 443 or 8443 ? "https" : "http";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{scheme}://{device.IpAddress}:{port}/");
                request.Headers.UserAgent.ParseAdd("CameraInspector/1.0");
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var serverHeader = response.Headers.Server?.ToString() ?? string.Empty;
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var haystack = (serverHeader + " " + body).ToLowerInvariant();

                foreach (var (needle, manufacturer) in Signatures)
                {
                    if (!haystack.Contains(needle, StringComparison.Ordinal))
                        continue;

                    return new ManufacturerDetectionResult
                    {
                        DetectorName = Name,
                        Confidence = manufacturer == "VIVOTEK" ? 0.95 : 0.75,
                        CameraEvidence = true,
                        EvidenceDetails = $"HTTP banner: {manufacturer}",
                        Manufacturer = manufacturer,
                        Model = ExtractModel(haystack, manufacturer),
                        HttpSupported = true,
                        HttpsSupported = scheme.Equals("https", StringComparison.OrdinalIgnoreCase),
                        HttpPort = port
                    };
                }

                if (CameraIndicators.Any(indicator => haystack.Contains(indicator, StringComparison.Ordinal)))
                {
                    return new ManufacturerDetectionResult
                    {
                        DetectorName = Name,
                        Confidence = 0.35,
                        CameraEvidence = true,
                        EvidenceDetails = "HTTP banner con indicadores de cámara",
                        HttpSupported = true,
                        HttpsSupported = scheme.Equals("https", StringComparison.OrdinalIgnoreCase),
                        HttpPort = port
                    };
                }

                // HTTP abierto es solo una capacidad de red, no evidencia de cámara.
                return new ManufacturerDetectionResult
                {
                    DetectorName = Name,
                    Confidence = 0.05,
                    CameraEvidence = false,
                    EvidenceDetails = "Servicio HTTP/HTTPS sin firma de cámara",
                    HttpSupported = true,
                    HttpsSupported = scheme.Equals("https", StringComparison.OrdinalIgnoreCase),
                    HttpPort = port
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout del puerto: continuar con el siguiente.
            }
            catch (HttpRequestException)
            {
                // El siguiente puerto puede ser el correcto.
            }
        }

        return null;
    }

    private static IEnumerable<int> GetHttpPortsToTry(DiscoveredDevice device)
    {
        if (device.HttpPort.HasValue)
            yield return device.HttpPort.Value;

        foreach (var port in new[] { 80, 81, 8080, 8081, 443, 8443, 8888 })
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
