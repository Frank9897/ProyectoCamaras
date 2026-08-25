using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Servicio ONVIF del dispositivo. Resuelve primero el XAddr real de Device Service
/// y utiliza una ruta convencional únicamente como fallback.
/// </summary>
public sealed class OnvifDeviceService : IOnvifDeviceService
{
    private readonly HttpClient _httpClient;

    public OnvifDeviceService(HttpClient httpClient)
    {
        // _httpClient reutiliza conexiones para las operaciones SOAP ONVIF.
        _httpClient = httpClient;
    }

    public async Task<OnvifDeviceInformation?> GetDeviceInformationAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveDeviceServiceEndpoint(device);
        if (endpoint is null)
            return null;

        var security = BuildSecurity(username, password);
        var body = """
                   <tds:GetDeviceInformation/>
                   """;

        var xml = await SendSoapAsync(endpoint, "http://www.onvif.org/ver10/device/wsdl/GetDeviceInformation", body, security, cancellationToken);
        if (xml is null)
            return null;

        var document = XDocument.Parse(xml);
        var element = document.Descendants().FirstOrDefault(item => item.Name.LocalName == "GetDeviceInformationResponse");
        if (element is null)
            return null;

        return new OnvifDeviceInformation
        {
            // Cada propiedad conserva el dato reportado por la cámara mediante GetDeviceInformation.
            Manufacturer = element.Elements().FirstOrDefault(item => item.Name.LocalName == "Manufacturer")?.Value,
            Model = element.Elements().FirstOrDefault(item => item.Name.LocalName == "Model")?.Value,
            FirmwareVersion = element.Elements().FirstOrDefault(item => item.Name.LocalName == "FirmwareVersion")?.Value,
            SerialNumber = element.Elements().FirstOrDefault(item => item.Name.LocalName == "SerialNumber")?.Value,
            HardwareId = element.Elements().FirstOrDefault(item => item.Name.LocalName == "HardwareId")?.Value
        };
    }

    public async Task<OnvifServiceCapabilities?> GetCapabilitiesAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveDeviceServiceEndpoint(device);
        if (endpoint is null)
            return null;

        var security = BuildSecurity(username, password);
        var body = """
                   <tds:GetCapabilities/>
                   """;

        var xml = await SendSoapAsync(endpoint, "http://www.onvif.org/ver10/device/wsdl/GetCapabilities", body, security, cancellationToken);
        if (xml is null)
            return null;

        var document = XDocument.Parse(xml);

        // Cada propiedad representa el XAddr real publicado por el dispositivo para ese servicio.
        return new OnvifServiceCapabilities
        {
            DeviceServiceXAddr = endpoint,
            MediaServiceXAddr = FindServiceXAddr(document, "Media"),
            ImagingServiceXAddr = FindServiceXAddr(document, "Imaging"),
            PtzServiceXAddr = FindServiceXAddr(document, "PTZ"),
            EventsServiceXAddr = FindServiceXAddr(document, "Events")
        };
    }

    public async Task<OnvifNetworkConfiguration?> GetNetworkConfigurationAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveDeviceServiceEndpoint(device);
        if (endpoint is null)
            return null;

        // credentials son obligatorias para estas consultas porque son operaciones READ_SYSTEM de Device Management.
        var security = BuildSecurity(username, password);

        var interfacesXml = await SendSoapAsync(
            endpoint,
            "http://www.onvif.org/ver10/device/wsdl/GetNetworkInterfaces",
            "<tds:GetNetworkInterfaces/>",
            security,
            cancellationToken);

        var protocolsXml = await SendSoapAsync(
            endpoint,
            "http://www.onvif.org/ver10/device/wsdl/GetNetworkProtocols",
            "<tds:GetNetworkProtocols/>",
            security,
            cancellationToken);

        var gatewayXml = await SendSoapAsync(
            endpoint,
            "http://www.onvif.org/ver10/device/wsdl/GetNetworkDefaultGateway",
            "<tds:GetNetworkDefaultGateway/>",
            security,
            cancellationToken);

        if (interfacesXml is null && protocolsXml is null && gatewayXml is null)
            return null;

        var configuration = new OnvifNetworkConfiguration();

        if (interfacesXml is not null)
            ParseNetworkInterfaces(interfacesXml, configuration);

        if (protocolsXml is not null)
            ParseNetworkProtocols(protocolsXml, configuration);

        if (gatewayXml is not null)
            ParseNetworkGateways(gatewayXml, configuration);

        return configuration;
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
            if (string.Equals(discoveredUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(discoveredUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return discoveredUri.ToString();
            }
        }

        // Fallback: algunas cámaras pueden funcionar con el endpoint convencional aunque todavía
        // no hayan sido descubiertas mediante WS-Discovery.
        if (!string.IsNullOrWhiteSpace(device.IpAddress))
            return $"http://{device.IpAddress}/onvif/device_service";

        return null;
    }

    /// <summary>Construye el encabezado WS-Security cuando existen credenciales.</summary>
    private static string? BuildSecurity(string? username, string? password) =>
        username is not null && password is not null
            ? WsSecurityHeaderBuilder.Build(username, password)
            : null;

    private async Task<string?> SendSoapAsync(
        string endpoint,
        string action,
        string body,
        string? security,
        CancellationToken cancellationToken)
    {
        // envelope contiene la solicitud SOAP completa que se enviará al Device Service.
        var envelope = $"""
                      <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                                  xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
                        <s:Header>{security ?? string.Empty}</s:Header>
                        <s:Body>{body}</s:Body>
                      </s:Envelope>
                      """;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("SOAPAction", action);
        request.Content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml");

        // response conserva la respuesta HTTP hasta que terminemos de leer su contenido.
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static void ParseNetworkInterfaces(string xml, OnvifNetworkConfiguration configuration)
    {
        var document = XDocument.Parse(xml);

        foreach (var element in document.Descendants().Where(item => item.Name.LocalName == "NetworkInterfaces"))
        {
            var token = (string?)element.Attribute("token");
            if (string.IsNullOrWhiteSpace(token))
                continue;

            var info = element.Elements().FirstOrDefault(item => item.Name.LocalName == "Info");
            var ipv4 = element.Descendants().FirstOrDefault(item => item.Name.LocalName == "IPv4");
            var ipv4Config = ipv4?.Descendants().FirstOrDefault(item => item.Name.LocalName == "Config");
            var dhcp = ParseNullableBool(ipv4Config?.Elements().FirstOrDefault(item => item.Name.LocalName == "DHCP")?.Value);
            var manual = ipv4Config?.Descendants().FirstOrDefault(item => item.Name.LocalName == "Manual");

            configuration.Interfaces.Add(new OnvifNetworkInterfaceInfo
            {
                Token = token,
                Enabled = ParseBool(element.Elements().FirstOrDefault(item => item.Name.LocalName == "Enabled")?.Value),
                Name = GetElementValue(info, "Name"),
                HwAddress = GetElementValue(info, "HwAddress"),
                Mtu = ParseInt(GetElementValue(info, "MTU")),
                IPv4Enabled = ParseNullableBool(ipv4?.Elements().FirstOrDefault(item => item.Name.LocalName == "Enabled")?.Value),
                IPv4Dhcp = dhcp,
                IPv4Address = GetElementValue(manual, "Address"),
                IPv4PrefixLength = ParseInt(GetElementValue(manual, "PrefixLength"))
            });
        }
    }

    private static void ParseNetworkProtocols(string xml, OnvifNetworkConfiguration configuration)
    {
        var document = XDocument.Parse(xml);

        foreach (var element in document.Descendants().Where(item => item.Name.LocalName == "NetworkProtocols"))
        {
            var name = GetElementValue(element, "Name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var item = new OnvifNetworkProtocolInfo
            {
                Name = name,
                Enabled = ParseBool(GetElementValue(element, "Enabled"))
            };

            foreach (var portNode in element.Elements().Where(node => node.Name.LocalName == "Port"))
            {
                if (int.TryParse(portNode.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
                    item.Ports.Add(port);
            }

            configuration.Protocols.Add(item);
        }
    }

    private static void ParseNetworkGateways(string xml, OnvifNetworkConfiguration configuration)
    {
        var document = XDocument.Parse(xml);

        foreach (var node in document.Descendants().Where(item => item.Name.LocalName == "IPv4Address"))
        {
            var value = node.Value.Trim();
            if (!string.IsNullOrWhiteSpace(value) && !configuration.IPv4Gateways.Contains(value, StringComparer.OrdinalIgnoreCase))
                configuration.IPv4Gateways.Add(value);
        }
    }

    private static string? GetElementValue(XElement? parent, string localName)
    {
        return parent?.Elements().FirstOrDefault(item => item.Name.LocalName == localName)?.Value?.Trim();
    }

    private static bool ParseBool(string? value) =>
        bool.TryParse(value, out var result) && result;

    private static bool? ParseNullableBool(string? value)
    {
        return bool.TryParse(value, out var result) ? result : null;
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static string? FindServiceXAddr(XDocument document, string serviceName)
    {
        // El parser busca el primer elemento cuyo nombre local coincida con el servicio ONVIF solicitado.
        return document.Descendants()
            .FirstOrDefault(item => item.Name.LocalName.Equals(serviceName, StringComparison.OrdinalIgnoreCase))?
            .Descendants()
            .FirstOrDefault(item => item.Name.LocalName.Equals("XAddr", StringComparison.OrdinalIgnoreCase))?
            .Value;
    }
}
