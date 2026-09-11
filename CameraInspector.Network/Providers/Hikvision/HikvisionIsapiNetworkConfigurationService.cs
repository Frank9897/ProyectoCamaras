using System.Net;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers.Hikvision;

/// <summary>
/// Compatibilidad de administración de red para cámaras/DVR HIKVISION (e ISAPI-compatibles,
/// como muchos OEM chinos) que no exponen ONVIF o cuyo ONVIF no permite escribir red.
/// A diferencia de VIVOTEK/DAHUA (CGI por query-string), ISAPI trabaja con XML sobre
/// HTTP GET/PUT, así que se hace un ciclo leer-modificar-escribir sobre el mismo documento
/// para no perder campos que la cámara exige y que esta app no edita (DNS, IPv6, MTU, etc.).
/// </summary>
public sealed class HikvisionIsapiNetworkConfigurationService : ILegacyCameraNetworkConfigurationService
{
    private static readonly XNamespace Ns = "http://www.hikvision.com/ver20/XMLSchema";

    public string DefaultAdminUsername => "admin";

    public async Task<OnvifNetworkChangeResult> SetHostnameAsync(
        DiscoveredDevice device,
        string username,
        string password,
        string hostname,
        CancellationToken cancellationToken = default)
    {
        hostname = hostname.Trim();
        if (string.IsNullOrWhiteSpace(hostname) || hostname.Length > 40)
            return Failure("El nombre de cámara no es válido para este firmware HIKVISION.");

        var endpoint = BuildHttpEndpoint(device, "/ISAPI/System/deviceInfo");
        var getResult = await SendGetAsync(endpoint, username, password, cancellationToken);
        if (!getResult.Success || string.IsNullOrWhiteSpace(getResult.Body))
            return Failure("No se pudo leer /ISAPI/System/deviceInfo antes de cambiar el nombre.");

        XDocument document;
        try { document = XDocument.Parse(getResult.Body); }
        catch { return Failure("La respuesta de ISAPI/System/deviceInfo no es un XML válido."); }

        var root = document.Root;
        if (root is null)
            return Failure("La cámara no expone /ISAPI/System/deviceInfo esperado.");

        SetOrAdd(root, "deviceName", hostname);

        var result = await SendPutAsync(endpoint, root.ToString(SaveOptions.DisableFormatting), username, password, cancellationToken);
        return result.Success
            ? new OnvifNetworkChangeResult { Succeeded = true, Message = "Nombre de cámara actualizado mediante ISAPI (deviceInfo)." }
            : Failure(result.Message);
    }

    public async Task<OnvifNetworkChangeResult> RebootAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var endpoint = BuildHttpEndpoint(device, "/ISAPI/System/reboot");
        var result = await SendPutAsync(endpoint, string.Empty, username, password, cancellationToken);
        return result.Success
            ? new OnvifNetworkChangeResult { Succeeded = true, Message = "La cámara aceptó la orden de reinicio mediante ISAPI." }
            : Failure(result.Message);
    }

    public async Task<OnvifNetworkChangeResult> FactoryResetAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        // "basic" preserva red/usuarios en la mayoría de firmwares HIKVISION;
        // "full"/"restore_default" borra todo, por eso no se ofrece acá.
        var endpoint = BuildHttpEndpoint(device, "/ISAPI/System/factoryReset?mode=basic");
        var result = await SendPutAsync(endpoint, string.Empty, username, password, cancellationToken);
        return result.Success
            ? new OnvifNetworkChangeResult { Succeeded = true, Message = "La cámara aceptó el restablecimiento básico mediante ISAPI." }
            : Failure(result.Message);
    }

    public async Task<OnvifNetworkChangeResult> SetAdminPasswordAsync(
        DiscoveredDevice device,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (newPassword.Length < 8)
            return Failure("HIKVISION exige contraseñas de al menos 8 caracteres, combinando letras y números.");

        // A diferencia de VIVOTEK/DAHUA, una cámara HIKVISION de fábrica no tiene
        // cuenta "admin" utilizable hasta ACTIVARSE: el primer acceso no es una
        // credencial en blanco, sino un endpoint distinto que crea la cuenta admin
        // con la contraseña elegida. Por eso, si currentPassword viene vacío,
        // se intenta activar en vez de "loguearse con blanco".
        if (string.IsNullOrEmpty(currentPassword))
        {
            var activateEndpoint = BuildHttpEndpoint(device, "/ISAPI/Security/activate");
            var activateBody = $"<ActivateReq><password>{System.Security.SecurityElement.Escape(newPassword)}</password></ActivateReq>";
            var activateResult = await SendPutAsync(activateEndpoint, activateBody, string.Empty, string.Empty, cancellationToken);

            if (activateResult.Success)
                return new OnvifNetworkChangeResult { Succeeded = true, Message = "Cámara HIKVISION activada con la nueva contraseña de admin." };

            // Si ISAPI/Security/activate no existe o la cámara ya está activada, HTTP 401
            // es la señal más común: se sigue al camino normal de cambio de contraseña.
            if (!activateResult.Message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase))
                return Failure($"No se pudo activar la cámara HIKVISION: {activateResult.Message}");
        }

        var usersEndpoint = BuildHttpEndpoint(device, "/ISAPI/Security/users/1");
        var body = $"""
            <User>
              <id>1</id>
              <userName>admin</userName>
              <password>{System.Security.SecurityElement.Escape(newPassword)}</password>
            </User>
            """;
        var result = await SendPutAsync(usersEndpoint, body, "admin", currentPassword, cancellationToken);

        return result.Success
            ? new OnvifNetworkChangeResult { Succeeded = true, Message = "Contraseña de admin actualizada mediante ISAPI (Security/users/1)." }
            : Failure(result.Message);
    }

    private static void SetOrAdd(XElement parent, string localName, string value)
    {
        var node = parent.Element(Ns + localName);
        if (node is null)
        {
            parent.Add(new XElement(Ns + localName, value));
            return;
        }
        node.Value = value;
    }

    public async Task<OnvifNetworkConfiguration?> GetNetworkConfigurationAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var (interfaceXml, _) = await GetInterfaceDocumentAsync(device, username, password, cancellationToken);
        if (interfaceXml is null)
            return null;

        var id = interfaceXml.Element(Ns + "id")?.Value ?? "1";
        var ipAddressNode = interfaceXml.Element(Ns + "IPAddress");
        var ip = ipAddressNode?.Element(Ns + "ipAddress")?.Value;
        var mask = ipAddressNode?.Element(Ns + "subnetMask")?.Value;
        var gateway = ipAddressNode?.Element(Ns + "DefaultGateway")?.Element(Ns + "ipAddress")?.Value;
        var addressingType = ipAddressNode?.Element(Ns + "addressingType")?.Value; // "static" o "dynamic"

        if (string.IsNullOrWhiteSpace(ip))
            return null;

        return new OnvifNetworkConfiguration
        {
            Hostname = null,
            Interfaces =
            [new OnvifNetworkInterfaceInfo
            {
                Token = $"legacy-hikvision-{id}",
                Enabled = true,
                Name = $"ISAPI interface {id}",
                HwAddress = device.MacAddress,
                Mtu = 1500,
                IPv4Enabled = true,
                IPv4Dhcp = string.Equals(addressingType, "dynamic", StringComparison.OrdinalIgnoreCase),
                IPv4Address = ip,
                IPv4PrefixLength = PrefixFromMask(mask)
            }],
            Protocols =
            [
                new OnvifNetworkProtocolInfo { Name = "HTTP / ISAPI", Enabled = true, Ports = [device.HttpPort ?? 80] },
                new OnvifNetworkProtocolInfo { Name = "RTSP", Enabled = device.RtspSupported, Ports = [device.RtspPort ?? 554] }
            ],
            IPv4Gateways = string.IsNullOrWhiteSpace(gateway) ? [] : [gateway]
        };
    }

    public async Task<OnvifNetworkChangeResult> SetNetworkAsync(
        DiscoveredDevice device,
        string username,
        string password,
        bool useDhcp,
        string? ipv4Address,
        int? prefixLength,
        string? gatewayAddress,
        CancellationToken cancellationToken = default)
    {
        if (!useDhcp && (string.IsNullOrWhiteSpace(ipv4Address) || prefixLength is null))
            return Failure("Para una IPv4 estática se requiere dirección y prefijo.");

        var (interfaceXml, id) = await GetInterfaceDocumentAsync(device, username, password, cancellationToken);
        if (interfaceXml is null || id is null)
            return Failure("No se pudo leer la configuración de red actual mediante ISAPI antes de modificarla.");

        // Editamos el mismo documento leído (round-trip) para no perder campos que
        // ISAPI exige en el PUT pero que esta app no administra (IPv6, DNS, MTU...).
        var ipAddressNode = interfaceXml.Element(Ns + "IPAddress");
        if (ipAddressNode is null)
            return Failure("La cámara no expone el nodo IPAddress esperado por ISAPI.");

        SetOrAdd(ipAddressNode, "addressingType", useDhcp ? "dynamic" : "static");
        if (!useDhcp)
        {
            SetOrAdd(ipAddressNode, "ipAddress", ipv4Address!.Trim());
            SetOrAdd(ipAddressNode, "subnetMask", MaskFromPrefix(prefixLength!.Value));
        }

        if (!string.IsNullOrWhiteSpace(gatewayAddress))
        {
            var gatewayNode = ipAddressNode.Element(Ns + "DefaultGateway");
            if (gatewayNode is null)
            {
                gatewayNode = new XElement(Ns + "DefaultGateway");
                ipAddressNode.Add(gatewayNode);
            }
            SetOrAdd(gatewayNode, "ipAddress", gatewayAddress.Trim());
        }

        var endpoint = BuildHttpEndpoint(device, $"/ISAPI/System/Network/interfaces/{id}");
        var result = await SendPutAsync(endpoint, interfaceXml.ToString(SaveOptions.DisableFormatting), username, password, cancellationToken);

        if (!result.Success)
        {
            if (!useDhcp
                && !string.IsNullOrWhiteSpace(ipv4Address)
                && !string.Equals(device.IpAddress, ipv4Address.Trim(), StringComparison.OrdinalIgnoreCase)
                && await WaitForHttpResponseAsync(ipv4Address.Trim(), device.HttpPort ?? 80, cancellationToken))
            {
                return new OnvifNetworkChangeResult
                {
                    Succeeded = true,
                    RebootNeeded = true,
                    Message = "La cámara/DVR aplicó la nueva IPv4 y ya responde en la dirección indicada. La conexión anterior quedó cerrada."
                };
            }

            return Failure(result.Message);
        }

        return new OnvifNetworkChangeResult
        {
            Succeeded = true,
            RebootNeeded = !useDhcp && !string.Equals(device.IpAddress, ipv4Address, StringComparison.OrdinalIgnoreCase),
            Message = "Configuración de red aceptada por ISAPI (HIKVISION)."
        };
    }

    private async Task<(XElement? InterfaceXml, string? Id)> GetInterfaceDocumentAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildHttpEndpoint(device, "/ISAPI/System/Network/interfaces");
        var result = await SendGetAsync(endpoint, username, password, cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Body))
            return (null, null);

        try
        {
            var document = XDocument.Parse(result.Body);
            // Puede venir como <NetworkInterfaceList> con varias <NetworkInterface>, o como
            // un único <NetworkInterface> si la cámara solo tiene una interfaz cableada.
            var first = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "NetworkInterface")
                        ?? document.Root;

            if (first is null)
                return (null, null);

            var id = first.Element(Ns + "id")?.Value
                     ?? first.Elements().FirstOrDefault(element => element.Name.LocalName == "id")?.Value
                     ?? "1";

            return (first, id);
        }
        catch
        {
            return (null, null);
        }
    }

    private static async Task<(bool Success, string Message, string Body)> SendGetAsync(
        string endpoint, string username, string password, CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            UseProxy = false,
            Credentials = new NetworkCredential(username ?? string.Empty, password ?? string.Empty),
            PreAuthenticate = false
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CameraInspector/1.0");

        try
        {
            using var response = await client.GetAsync(endpoint, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return (false, "HTTP 401 Unauthorized: ISAPI exige credenciales administrativas.", body);
            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", body);
            return (true, "OK", body);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "Tiempo de espera agotado al comunicarse con ISAPI.", string.Empty);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"No se pudo conectar con ISAPI: {ex.Message}", string.Empty);
        }
    }

    private static async Task<(bool Success, string Message)> SendPutAsync(
        string endpoint, string xmlBody, string username, string password, CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            UseProxy = false,
            Credentials = new NetworkCredential(username ?? string.Empty, password ?? string.Empty),
            PreAuthenticate = false
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CameraInspector/1.0");

        try
        {
            using var content = new StringContent(xmlBody, System.Text.Encoding.UTF8, "application/xml");
            using var response = await client.PutAsync(endpoint, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return (false, "HTTP 401 Unauthorized: ISAPI exige credenciales administrativas para escribir red.");
            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            var bodyLower = body.ToLowerInvariant();
            if (bodyLower.Contains("<statuscode>1<") || bodyLower.Contains(">ok<"))
                return (true, "OK");
            if (bodyLower.Contains("<statuscode>") )
                return (false, "ISAPI devolvió un código de estado distinto de éxito al aplicar la red.");

            // Algunos firmwares no devuelven cuerpo si aceptan el cambio y cortan la conexión.
            return (true, "OK (sin cuerpo de confirmación)");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "Tiempo de espera agotado al aplicar la red mediante ISAPI.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"No se pudo conectar con ISAPI: {ex.Message}");
        }
    }

    private static async Task<bool> WaitForHttpResponseAsync(string ip, int port, CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1.5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CameraInspector/1.0");
        var endpoint = BuildHttpEndpoint(ip, port, "/ISAPI/System/deviceInfo");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(endpoint, cancellationToken);
                return true;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            await Task.Delay(350, cancellationToken);
        }
        return false;
    }

    private static OnvifNetworkChangeResult Failure(string message) => new() { Succeeded = false, Message = message };

    private static string BuildHttpEndpoint(DiscoveredDevice device, string relativePath) =>
        BuildHttpEndpoint(device.IpAddress.Trim(), device.HttpPort ?? 80, relativePath);

    private static string BuildHttpEndpoint(string ip, int port, string relativePath) =>
        $"http://{ip}:{port}{relativePath}";

    private static int? PrefixFromMask(string? mask)
    {
        if (!IPAddress.TryParse(mask, out var parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return 24;

        var bytes = parsed.GetAddressBytes();
        var prefix = 0;
        foreach (var b in bytes)
        {
            var value = b;
            for (var bit = 7; bit >= 0 && (value & (1 << bit)) != 0; bit--)
                prefix++;
        }
        return prefix is >= 1 and <= 32 ? prefix : 24;
    }

    private static string MaskFromPrefix(int prefix)
    {
        prefix = Math.Clamp(prefix, 1, 32);
        var mask = prefix == 32 ? uint.MaxValue : uint.MaxValue << (32 - prefix);
        var bytes = new byte[] { (byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask };
        return new IPAddress(bytes).ToString();
    }
}
