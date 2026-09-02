using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detector ONVIF activo. Confirma el dispositivo mediante GetDeviceInformation.
/// </summary>
public sealed class OnvifProbeDetector : IManufacturerDetector
{
    public string Name => "OnvifProbe";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMilliseconds(1200)
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
        try
        {
            var endpoint = $"http://{device.IpAddress}/onvif/device_service";
            using var content = new StringContent(SoapEnvelope, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml");
            using var response = await Http.PostAsync(endpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = XDocument.Parse(xml);

            string? Get(string localName) =>
                doc.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                    ?.Value;

            var manufacturer = Get("Manufacturer");
            if (string.IsNullOrWhiteSpace(manufacturer)) return null;

            return new ManufacturerDetectionResult
            {
                DetectorName = Name,
                Confidence = 0.95,
                CameraEvidence = true,
                EvidenceDetails = "ONVIF GetDeviceInformation",
                Manufacturer = manufacturer.Trim(),
                Model = Get("Model")?.Trim(),
                FirmwareVersion = Get("FirmwareVersion")?.Trim(),
                SerialNumber = Get("SerialNumber")?.Trim(),
                OnvifSupported = true,
                OnvifProfile = "detectado",
                OnvifDeviceServiceXAddr = endpoint,
                HttpSupported = true,
                HttpPort = 80
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
