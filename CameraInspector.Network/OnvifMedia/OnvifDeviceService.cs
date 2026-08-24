using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Consulta el Device Service ONVIF y descubre las URLs reales de Media, Imaging,
/// PTZ y Events anunciadas por el firmware mediante GetCapabilities.
/// </summary>
public sealed class OnvifDeviceService : IOnvifDeviceService
{
    /// <summary>
    /// Cuerpo SOAP utilizado para pedir al dispositivo las capacidades de todos sus servicios ONVIF.
    /// </summary>
    private const string GetCapabilitiesBody = """
        <tds:GetCapabilities xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
          <tds:Category>All</tds:Category>
        </tds:GetCapabilities>
        """;

    public async Task<OnvifServiceCapabilities?> GetCapabilitiesAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        // Primero intentamos reutilizar el Device Service que ya fue detectado por la Capa 4.
        // Esto evita reconstruir una URL que puede no coincidir con la publicada por el firmware.
        var endpoint = device.OnvifDeviceServiceXAddr;

        // Si todavía no existe un XAddr registrado, usamos el endpoint convencional como fallback.
        // Este fallback mantiene compatible el flujo actual mientras migramos todo el descubrimiento
        // hacia WS-Discovery y XAddr reales.
        if (string.IsNullOrWhiteSpace(endpoint)
            && !string.IsNullOrWhiteSpace(device.IpAddress))
        {
            endpoint = $"http://{device.IpAddress}/onvif/device_service";
        }

        // Sin IP ni XAddr no existe un destino válido para ejecutar GetCapabilities.
        if (string.IsNullOrWhiteSpace(endpoint))
            return null;

        // security contiene el encabezado WS-Security cuando el técnico proporcionó credenciales.
        // Si no hay usuario/contraseña, el valor permanece null y el SOAP se envía sin autenticación.
        var security = (username, password) is (not null, not null)
            ? WsSecurityHeaderBuilder.Build(username!, password!)
            : null;

        // document contiene la respuesta XML de GetCapabilities. OnvifSoapClient ya se ocupa
        // del transporte HTTP, del SOAP envelope y de convertir la respuesta en XDocument.
        var document = await OnvifSoapClient.PostAsync(
            endpoint,
            GetCapabilitiesBody,
            security,
            cancellationToken);

        // Si el dispositivo no respondió correctamente, no inventamos capacidades.
        if (document is null)
            return null;

        // Los XAddr devueltos por el firmware representan las URLs reales que exponen cada servicio.
        return new OnvifServiceCapabilities
        {
            // DeviceServiceXAddr conserva el endpoint que efectivamente utilizamos para esta consulta.
            DeviceServiceXAddr = endpoint,

            // MediaServiceXAddr permite a la Capa de Media consultar perfiles y streams sin asumir rutas.
            MediaServiceXAddr = FindServiceXAddr(document, "Media"),

            // ImagingServiceXAddr queda disponible para futuras funciones de imagen.
            ImagingServiceXAddr = FindServiceXAddr(document, "Imaging"),

            // PtzServiceXAddr queda disponible para futuras funciones PTZ.
            PtzServiceXAddr = FindServiceXAddr(document, "PTZ"),

            // EventsServiceXAddr queda disponible para futuras funciones de eventos/alarmas.
            EventsServiceXAddr = FindServiceXAddr(document, "Events")
        };
    }

    /// <summary>
    /// Busca un bloque de servicio por nombre local y devuelve su XAddr sin depender
    /// de los prefijos XML utilizados por el fabricante.
    /// </summary>
    private static string? FindServiceXAddr(
        System.Xml.Linq.XDocument document,
        string serviceElementName)
    {
        // service representa el nodo cuyo nombre local coincide con el servicio solicitado
        // y que contiene un hijo XAddr.
        var service = document
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(
                    element.Name.LocalName,
                    serviceElementName,
                    StringComparison.OrdinalIgnoreCase)
                && element.Elements().Any(child =>
                    string.Equals(
                        child.Name.LocalName,
                        "XAddr",
                        StringComparison.OrdinalIgnoreCase)));

        // El valor devuelto es el XAddr publicado por el firmware. Si no existe, devolvemos null.
        return service?
            .Elements()
            .FirstOrDefault(child =>
                string.Equals(
                    child.Name.LocalName,
                    "XAddr",
                    StringComparison.OrdinalIgnoreCase))?
            .Value
            .Trim();
    }
}
