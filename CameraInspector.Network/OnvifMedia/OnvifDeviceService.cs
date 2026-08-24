using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Consulta el Device Service ONVIF.
/// Permite obtener identidad del dispositivo y descubrir las URLs reales de sus servicios.
/// </summary>
public sealed class OnvifDeviceService : IOnvifDeviceService
{
    /// <summary>Cuerpo SOAP utilizado para obtener información básica del dispositivo.</summary>
    private const string GetDeviceInformationBody = """
        <tds:GetDeviceInformation xmlns:tds="http://www.onvif.org/ver10/device/wsdl"/>
        """;

    /// <summary>
    /// Cuerpo SOAP utilizado para pedir al dispositivo las capacidades de todos sus servicios ONVIF.
    /// </summary>
    private const string GetCapabilitiesBody = """
        <tds:GetCapabilities xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
          <tds:Category>All</tds:Category>
        </tds:GetCapabilities>
        """;

    /// <summary>
    /// Ejecuta GetDeviceInformation y devuelve los datos de identidad publicados por ONVIF.
    /// </summary>
    public async Task<OnvifDeviceInformation?> GetDeviceInformationAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        // endpoint contiene la dirección real del Device Service cuando ya fue descubierta por WS-Discovery.
        var endpoint = ResolveDeviceServiceEndpoint(device);
        if (endpoint is null)
            return null;

        // securityHeader contiene WS-Security únicamente cuando el usuario proporcionó credenciales.
        // Si no existen credenciales, permanece en null y OnvifSoapClient enviará el SOAP sin autenticación.
        var securityHeader = BuildSecurity(username, password);

        // document contiene la respuesta XML de GetDeviceInformation.
        var document = await OnvifSoapClient.PostAsync(
            endpoint,
            GetDeviceInformationBody,
            securityHeader,
            cancellationToken);

        if (document is null)
            return null;

        // Cada variable siguiente representa un dato de identidad que el firmware puede proporcionar.
        // GetByLocalName evita depender de los prefijos XML concretos utilizados por la cámara.
        var manufacturer = GetByLocalName(document, "Manufacturer");
        var model = GetByLocalName(document, "Model");
        var firmwareVersion = GetByLocalName(document, "FirmwareVersion");
        var serialNumber = GetByLocalName(document, "SerialNumber");
        var hardwareId = GetByLocalName(document, "HardwareId");

        // Si no obtenemos ningún dato útil, tratamos la respuesta como no válida para esta operación.
        if (string.IsNullOrWhiteSpace(manufacturer)
            && string.IsNullOrWhiteSpace(model)
            && string.IsNullOrWhiteSpace(firmwareVersion)
            && string.IsNullOrWhiteSpace(serialNumber)
            && string.IsNullOrWhiteSpace(hardwareId))
        {
            return null;
        }

        return new OnvifDeviceInformation
        {
            Manufacturer = Normalize(manufacturer),
            Model = Normalize(model),
            FirmwareVersion = Normalize(firmwareVersion),
            SerialNumber = Normalize(serialNumber),
            HardwareId = Normalize(hardwareId)
        };
    }

    /// <summary>
    /// Consulta GetCapabilities y descubre los XAddr reales de los servicios ONVIF.
    /// </summary>
    public async Task<OnvifServiceCapabilities?> GetCapabilitiesAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        // endpoint contiene el Device Service que debe procesar GetCapabilities.
        var endpoint = ResolveDeviceServiceEndpoint(device);
        if (endpoint is null)
            return null;

        // securityHeader se calcula por cada operación para utilizar un nonce/timestamp actualizado.
        var securityHeader = BuildSecurity(username, password);

        // document contiene la respuesta XML de GetCapabilities.
        var document = await OnvifSoapClient.PostAsync(
            endpoint,
            GetCapabilitiesBody,
            securityHeader,
            cancellationToken);

        if (document is null)
            return null;

        return new OnvifServiceCapabilities
        {
            // Conservamos exactamente el Device Service utilizado por esta operación.
            DeviceServiceXAddr = endpoint,

            // MediaServiceXAddr será utilizado por la capa Media para descubrir perfiles y streams.
            MediaServiceXAddr = FindServiceXAddr(document, "Media"),

            // ImagingServiceXAddr queda preparado para configuración de imagen.
            ImagingServiceXAddr = FindServiceXAddr(document, "Imaging"),

            // PtzServiceXAddr queda preparado para cámaras PTZ.
            PtzServiceXAddr = FindServiceXAddr(document, "PTZ"),

            // EventsServiceXAddr queda preparado para eventos y alarmas.
            EventsServiceXAddr = FindServiceXAddr(document, "Events")
        };
    }

    /// <summary>
    /// Resuelve el endpoint prioritariamente desde WS-Discovery y mantiene un fallback
    /// convencional para dispositivos que todavía no fueron descubiertos mediante XAddr.
    /// </summary>
    private static string? ResolveDeviceServiceEndpoint(DiscoveredDevice device)
    {
        // endpoint es la dirección que reutilizaremos para todas las operaciones del Device Service.
        if (!string.IsNullOrWhiteSpace(device.OnvifDeviceServiceXAddr)
            && Uri.TryCreate(device.OnvifDeviceServiceXAddr, UriKind.Absolute, out var discoveredUri))
        {
            // Solo aceptamos HTTP/HTTPS porque las operaciones SOAP posteriores dependen de transporte web.
            if (discoveredUri.Scheme is Uri.UriSchemeHttp or Uri.UriSchemeHttps)
                return discoveredUri.ToString();
        }

        // Fallback: algunas cámaras pueden funcionar con el endpoint convencional aunque todavía
        // no hayan sido descubiertas mediante WS-Discovery.
        if (!string.IsNullOrWhiteSpace(device.IpAddress))
            return $"http://{device.IpAddress}/onvif/device_service";

        return null;
    }

    /// <summary>Construye el encabezado WS-Security cuando existen credenciales.</summary>
    private static string? BuildSecurity(string? username, string? password) =>
        (username, password) is (not null, not null)
            ? WsSecurityHeaderBuilder.Build(username!, password!)
            : null;

    /// <summary>
    /// Busca el primer elemento que coincida con un nombre local XML.
    /// </summary>
    private static string? GetByLocalName(XDocument document, string localName) =>
        document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == localName)
            ?.Value;

    /// <summary>Convierte una cadena vacía o compuesta solo por espacios en null.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Busca un bloque de servicio por nombre local y devuelve su XAddr.
    /// </summary>
    private static string? FindServiceXAddr(XDocument document, string serviceElementName)
    {
        // service representa el nodo que contiene el nombre del servicio y un hijo XAddr.
        var service = document
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, serviceElementName, StringComparison.OrdinalIgnoreCase)
                && element.Elements().Any(child =>
                    string.Equals(child.Name.LocalName, "XAddr", StringComparison.OrdinalIgnoreCase)));

        // El resultado es exactamente la URL que el firmware publica para el servicio solicitado.
        return service?
            .Elements()
            .FirstOrDefault(child =>
                string.Equals(child.Name.LocalName, "XAddr", StringComparison.OrdinalIgnoreCase))?
            .Value
            .Trim();
    }
}
