using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detector de mayor confianza: intenta un GetDeviceInformation ONVIF real contra el
/// endpoint conocido durante esta fase del MVP. Cuando obtiene una respuesta válida,
/// conserva también la dirección del Device Service para que las capas posteriores
/// reutilicen el endpoint detectado.
/// </summary>
public sealed class OnvifProbeDetector : IManufacturerDetector
{
    /// <summary>Nombre identificable de este detector dentro del sistema de resolución.</summary>
    public string Name => "OnvifProbe";

    /// <summary>
    /// Cliente HTTP reutilizable para las pruebas ONVIF de descubrimiento.
    /// El timeout corto evita bloquear el escaneo cuando una IP no ofrece ONVIF.
    /// </summary>
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMilliseconds(1200)
    };

    /// <summary>
    /// Cuerpo SOAP mínimo utilizado para pedir la información básica del dispositivo ONVIF.
    /// </summary>
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
            // endpoint es la URL del Device Service que estamos intentando consultar.
            // Se conserva con el mismo valor que utilizaremos en la respuesta para que
            // las capas posteriores no tengan que reconstruir la URL desde la IP.
            var endpoint = $"http://{device.IpAddress}/onvif/device_service";

            // content contiene el SOAP que se envía al dispositivo.
            using var content = new StringContent(SoapEnvelope, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml");

            // response representa la respuesta HTTP del dispositivo. Si la cámara no
            // ofrece ONVIF en este endpoint, la operación termina por timeout o error HTTP.
            using var response = await Http.PostAsync(
                endpoint,
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            // xml contiene el cuerpo SOAP devuelto por la cámara como texto.
            var xml = await response.Content.ReadAsStringAsync(cancellationToken);

            // doc transforma el XML recibido en una estructura navegable independientemente
            // de los prefijos de namespace utilizados por el fabricante.
            var doc = XDocument.Parse(xml);

            // Get busca un elemento por LocalName y devuelve su contenido sin depender
            // de que el firmware use prefijos como tds, tt, trt u otros.
            string? Get(string localName) =>
                doc.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == localName)
                    ?.Value;

            // manufacturer contiene la marca reportada directamente por ONVIF.
            // Si no existe, consideramos que la respuesta no es una respuesta ONVIF válida.
            var manufacturer = Get("Manufacturer");
            if (string.IsNullOrWhiteSpace(manufacturer))
                return null;

            return new ManufacturerDetectionResult
            {
                DetectorName = Name,
                Confidence = 0.95,
                Manufacturer = manufacturer.Trim(),
                Model = Get("Model")?.Trim(),
                FirmwareVersion = Get("FirmwareVersion")?.Trim(),
                SerialNumber = Get("SerialNumber")?.Trim(),
                OnvifSupported = true,
                OnvifProfile = "detectado",

                // OnvifDeviceServiceXAddr conserva la URL exacta que acabamos de utilizar.
                OnvifDeviceServiceXAddr = endpoint,

                HttpSupported = true,
                HttpPort = 80
            };
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            // Cualquier fallo aislado de este detector significa únicamente "no detectado".
            // Nunca dejamos que una IP problemática detenga el escaneo del resto de la red.
            return null;
        }
    }
}
