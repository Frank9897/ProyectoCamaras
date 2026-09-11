using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers.Dahua;

/// <summary>
/// Compatibilidad de administración de red para cámaras/DVR DAHUA (y OEM compatibles,
/// como Amcrest) que no exponen ONVIF o cuyo ONVIF no soporta escritura de red.
/// Usa el CGI "configManager" clásico (mismo estilo query-string que VIVOTEK legacy).
/// </summary>
public sealed class DahuaLegacyConfigurationService : ILegacyCameraNetworkConfigurationService
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    public async Task<OnvifNetworkConfiguration?> GetNetworkConfigurationAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        var endpoint = BuildHttpEndpoint(device, "/cgi-bin/configManager.cgi?action=getConfig&name=Network");
        var result = await SendAsync(endpoint, username, password, cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Body))
            return null;

        var values = ParseTableResponse(result.Body);

        // El CGI de Dahua puede exponer la interfaz como "table.Network.eth0.*" (más común)
        // o, en equipos con varias interfaces, "table.Network.eth0.*"/"table.Network.eth1.*".
        // Se usa siempre eth0 (interfaz cableada principal), que es la que administra esta app.
        var ip = GetValue(values, "table.Network.eth0.IPAddress");
        var mask = GetValue(values, "table.Network.eth0.SubnetMask");
        var gateway = GetValue(values, "table.Network.eth0.DefaultGateway");
        var dhcpRaw = GetValue(values, "table.Network.eth0.DhcpEnable");
        var hostname = GetValue(values, "table.NetCommon.HostName");

        if (string.IsNullOrWhiteSpace(ip))
            return null;

        return new OnvifNetworkConfiguration
        {
            Hostname = hostname,
            Interfaces =
            [new OnvifNetworkInterfaceInfo
            {
                Token = "legacy-dahua-eth0",
                Enabled = true,
                Name = "eth0",
                HwAddress = device.MacAddress,
                Mtu = 1500,
                IPv4Enabled = true,
                IPv4Dhcp = string.Equals(dhcpRaw, "true", StringComparison.OrdinalIgnoreCase),
                IPv4Address = ip,
                IPv4PrefixLength = PrefixFromMask(mask)
            }],
            Protocols =
            [
                new OnvifNetworkProtocolInfo { Name = "HTTP / DHIP CGI", Enabled = true, Ports = [device.HttpPort ?? 80] },
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
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return Failure("La cámara no tiene una dirección IP válida.");

        if (!useDhcp && (string.IsNullOrWhiteSpace(ipv4Address) || prefixLength is null))
            return Failure("Para una IPv4 estática se requiere dirección y prefijo.");

        var query = new List<string>
        {
            "action=setConfig",
            $"Network.eth0.DhcpEnable={(useDhcp ? "true" : "false")}"
        };

        if (!useDhcp)
        {
            query.Add($"Network.eth0.IPAddress={Uri.EscapeDataString(ipv4Address!.Trim())}");
            query.Add($"Network.eth0.SubnetMask={Uri.EscapeDataString(MaskFromPrefix(prefixLength!.Value))}");
        }

        if (!string.IsNullOrWhiteSpace(gatewayAddress))
            query.Add($"Network.eth0.DefaultGateway={Uri.EscapeDataString(gatewayAddress.Trim())}");

        var endpoint = BuildHttpEndpoint(device, $"/cgi-bin/configManager.cgi?{string.Join("&", query)}");
        var result = await SendAsync(endpoint, username, password, cancellationToken);

        if (!result.Success)
        {
            // Igual que en VIVOTEK: si cambiamos la IP, es normal que el DVR/cámara corte la
            // conexión antes de responder 200 OK. Verificamos si ya está respondiendo en la IP nueva.
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
            Message = "Configuración de red aceptada por el CGI DAHUA (configManager)."
        };
    }

    private static Dictionary<string, string> ParseTableResponse(string body)
    {
        // Formato típico: "table.Network.eth0.IPAddress=192.168.1.10\r\n..."
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(name))
                values[name] = value;
        }
        return values;
    }

    private static async Task<(bool Success, string Message, string Body)> SendAsync(
        string endpoint,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            UseProxy = false,
            Credentials = new NetworkCredential(username ?? string.Empty, password ?? string.Empty),
            PreAuthenticate = false,
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.ConnectionClose = true;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CameraInspector/1.0");

        try
        {
            using var response = await client.GetAsync(endpoint, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return (false, "HTTP 401 Unauthorized: la cámara/DVR exige credenciales administrativas para esta operación.", body);

            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", body);

            var bodyLower = body.ToLowerInvariant();
            if (bodyLower.Contains("error") && !bodyLower.Contains("ok"))
                return (false, "El CGI DAHUA informó un error al procesar la operación.", body);

            return (true, "OK", body);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "Tiempo de espera agotado al comunicarse con el CGI DAHUA.", string.Empty);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"No se pudo conectar con el CGI DAHUA: {ex.Message}", string.Empty);
        }
    }

    private static async Task<bool> WaitForHttpResponseAsync(string ip, int port, CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(1.5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CameraInspector/1.0");
        var endpoint = BuildHttpEndpoint(ip, port, "/cgi-bin/magicBox.cgi?action=getDeviceType");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(endpoint, cancellationToken);
                if (response.IsSuccessStatusCode) return true;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            await Task.Delay(350, cancellationToken);
        }
        return false;
    }

    private static string? GetValue(Dictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) ? value : null;

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
