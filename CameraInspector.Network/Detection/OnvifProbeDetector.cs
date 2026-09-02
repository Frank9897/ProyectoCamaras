using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detector ONVIF activo. Confirma el dispositivo mediante GetDeviceInformation.
/// No depende de que WS-Discovery haya respondido previamente.
/// </summary>
public sealed class OnvifProbeDetector : IManufacturerDetector
{
    public string Name => "OnvifProbe";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMilliseconds(1200)
    };

    private static readonly int[] DefaultPorts =
    {
        80, 81, 82, 88, 443, 8000, 8080, 8081, 8443, 8888, 8899
    };

    private const string SoapEnvelope = """
        <?xml version="1.0" encoding="UTF-8"?>
        <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"
                       xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
          <soap:Body>
            <tds:GetDeviceInformation/>
          </soap:Body>
        </soap:Envelope>
        """;

    public async Task<ManufacturerDetectionResult?> TryDetectAsync(
        DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        foreach (var port in GetPortsToTry(device))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isHttps = port is 443 or 8443;
            var scheme = isHttps ? "https" : "http";
            var endpoint = $"{scheme}://{device.IpAddress}:{port}/onvif/device_service";

            try
            {
                using var content = new StringContent(SoapEnvelope, Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml");
                using var response = await Http.PostAsync(endpoint, content, cancellationToken);

                // 401/403 sigue siendo útil: el endpoint ONVIF respondió pero exige acceso.
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    return new ManufacturerDetectionResult
                    {
                        DetectorName = Name,
                        Confidence = 0.92,
                        CameraEvidence = true,
                        EvidenceDetails = $"Endpoint ONVIF respondió {((int)response.StatusCode)} en {endpoint}",
                        OnvifSupported = true,
                        OnvifProfile = "endpoint detectado",
                        OnvifDeviceServiceXAddr = endpoint,
                        HttpSupported = !isHttps,
                        HttpsSupported = isHttps,
                        HttpPort = isHttps ? null : port
                    };
                }

                if (!response.IsSuccessStatusCode)
                    continue;

                var xml = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = XDocument.Parse(xml);

                string? Get(string localName) =>
                    doc.Descendants()
                        .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                        ?.Value;

                var manufacturer = Get("Manufacturer");
                if (string.IsNullOrWhiteSpace(manufacturer))
                    continue;

                return new ManufacturerDetectionResult
                {
                    DetectorName = Name,
                    Confidence = 0.99,
                    CameraEvidence = true,
                    EvidenceDetails = $"ONVIF GetDeviceInformation en {endpoint}",
                    Manufacturer = manufacturer.Trim(),
                    Model = Get("Model")?.Trim(),
                    FirmwareVersion = Get("FirmwareVersion")?.Trim(),
                    SerialNumber = Get("SerialNumber")?.Trim(),
                    OnvifSupported = true,
                    OnvifProfile = "detectado",
                    OnvifDeviceServiceXAddr = endpoint,
                    HttpSupported = !isHttps,
                    HttpsSupported = isHttps,
                    HttpPort = isHttps ? null : port
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout del puerto: probar el siguiente endpoint.
            }
            catch (HttpRequestException)
            {
                // El siguiente puerto puede ser el correcto.
            }
            catch (System.Xml.XmlException)
            {
                // Respuesta HTTP que no es XML ONVIF: seguir probando.
            }
        }

        return null;
    }

    private static IEnumerable<int> GetPortsToTry(DiscoveredDevice device)
    {
        if (device.HttpPort is int configured)
            yield return configured;

        foreach (var port in DefaultPorts)
        {
            if (port != device.HttpPort)
                yield return port;
        }
    }
}
