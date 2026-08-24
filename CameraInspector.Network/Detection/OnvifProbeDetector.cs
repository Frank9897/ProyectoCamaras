using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detector de mayor confianza: intenta un GetDeviceInformation ONVIF real contra el
/// endpoint estándar (/onvif/device_service). Se implementa con SOAP crudo por HTTP
/// para no atar este MVP a una librería ONVIF completa todavía (eso llega en la Capa 5,
/// cuando construyamos IOnvifCameraService con soporte completo de Device/Media/PTZ/Events).
/// Si el dispositivo responde esto correctamente, es casi seguro que es una cámara/NVR ONVIF.
/// </summary>
public sealed class OnvifProbeDetector : IManufacturerDetector
{
    public string Name => "OnvifProbe";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(1200) };

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
            using var content = new StringContent(SoapEnvelope, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml");

            using var response = await Http.PostAsync(
                $"http://{device.IpAddress}/onvif/device_service", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = XDocument.Parse(xml);

            // Se busca por LocalName para no depender de qué prefijo de namespace use cada firmware.
            string? Get(string localName) =>
                doc.Descendants().FirstOrDefault(el => el.Name.LocalName == localName)?.Value;

            var manufacturer = Get("Manufacturer");
            if (string.IsNullOrWhiteSpace(manufacturer))
                return null; // respondió algo, pero no es una respuesta ONVIF válida

            return new ManufacturerDetectionResult
            {
                DetectorName = Name,
                Confidence = 0.95,
                Manufacturer = manufacturer.Trim(),
                Model = Get("Model")?.Trim(),
                FirmwareVersion = Get("FirmwareVersion")?.Trim(),
                SerialNumber = Get("SerialNumber")?.Trim(),
                OnvifSupported = true,
                OnvifProfile = "detectado", // el perfil exacto (S/T/G) se determina en Capa 5
                HttpSupported = true,
                HttpPort = 80
            };
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            // Sin servicio ONVIF en ese puerto/endpoint, timeout, o XML inesperado: resultado = "nada".
            return null;
        }
    }
}
