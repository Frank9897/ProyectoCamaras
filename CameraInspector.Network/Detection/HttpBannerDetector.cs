using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detector basado en la respuesta HTTP del dispositivo (header "Server" + cuerpo de la
/// página de login). Confianza media: confirma que el puerto HTTP responde y suele
/// identificar la marca, pero un proxy o gateway podría enmascarar el banner real.
/// </summary>
public sealed class HttpBannerDetector : IManufacturerDetector
{
    public string Name => "HttpBanner";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(800) };

    // Firmas de texto conocidas en el header Server o en el HTML de login de cada fabricante.
    private static readonly (string Needle, string Manufacturer)[] Signatures =
    {
        ("hikvision", "Hikvision"),
        ("dahua", "Dahua"),
        ("dvrdvs", "Dahua"), // algunos firmwares Dahua usan este server-string
        ("axis", "Axis"),
        ("uniview", "Uniview"),
        ("reolink", "Reolink"),
    };

    public async Task<ManufacturerDetectionResult?> TryDetectAsync(
        DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http.GetAsync($"http://{device.IpAddress}/", cancellationToken);

            var serverHeader = response.Headers.Server?.ToString() ?? string.Empty;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var haystack = (serverHeader + " " + body).ToLowerInvariant();

            foreach (var (needle, manufacturer) in Signatures)
            {
                if (haystack.Contains(needle))
                {
                    return new ManufacturerDetectionResult
                    {
                        DetectorName = Name,
                        Confidence = 0.7,
                        Manufacturer = manufacturer,
                        HttpSupported = true,
                        HttpPort = 80
                    };
                }
            }

            // Respondió HTTP pero no matcheó ninguna firma conocida: igual es información útil
            // (confirma que el puerto está vivo), con confianza muy baja en cuanto a fabricante.
            return new ManufacturerDetectionResult
            {
                DetectorName = Name,
                Confidence = 0.1,
                HttpSupported = true,
                HttpPort = 80
            };
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            // Timeout, conexión rechazada, sin servidor HTTP en ese puerto: resultado válido = "nada".
            return null;
        }
    }
}
