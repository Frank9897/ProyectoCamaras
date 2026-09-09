using System.Net;
using System.Text;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers.Vivotek;

/// <summary>
/// Compatibilidad de administración para cámaras VIVOTEK legacy que no exponen
/// correctamente la configuración por ONVIF, como la familia IP7133/IP7134.
/// </summary>
public sealed class VivotekLegacyConfigurationService
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(8);

    public async Task<OnvifNetworkConfiguration?> GetNetworkConfigurationAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        var parameters = await GetParametersAsync(device.IpAddress, username, password, cancellationToken);
        if (parameters.Count == 0)
            return null;

        var ip = GetValue(parameters, "network.ipaddress");
        var subnet = GetValue(parameters, "network.subnet");
        var gateway = GetValue(parameters, "network.router");
        var dhcp = GetBoolean(parameters, "network.resetip");
        if (string.IsNullOrWhiteSpace(ip))
            return null;

        return new OnvifNetworkConfiguration
        {
            Hostname = GetValue(parameters, "system.hostname"),
            Interfaces =
            [new OnvifNetworkInterfaceInfo
            {
                Token = "legacy-vivotek",
                Enabled = true,
                Name = "Ethernet",
                HwAddress = device.MacAddress,
                Mtu = 1500,
                IPv4Enabled = true,
                IPv4Dhcp = dhcp,
                IPv4Address = ip,
                IPv4PrefixLength = PrefixFromMask(subnet)
            }],
            Protocols =
            [
                new OnvifNetworkProtocolInfo
                {
                    Name = "HTTP / CGI",
                    Enabled = true,
                    Ports = [device.HttpPort ?? 80]
                },
                new OnvifNetworkProtocolInfo
                {
                    Name = "RTSP",
                    Enabled = device.RtspSupported,
                    Ports = [device.RtspPort ?? 554]
                }
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

        var query = new List<string> { $"network_resetip={(useDhcp ? "1" : "0")}" };
        if (!useDhcp)
        {
            query.Add($"network_ipaddress={Uri.EscapeDataString(ipv4Address!.Trim())}");
            query.Add($"network_subnet={Uri.EscapeDataString(MaskFromPrefix(prefixLength!.Value))}");
        }
        if (!string.IsNullOrWhiteSpace(gatewayAddress))
            query.Add($"network_router={Uri.EscapeDataString(gatewayAddress.Trim())}");
        query.Add("update=1");

        var endpoint = $"http://{device.IpAddress.Trim()}/cgi-bin/admin/setparam.cgi?{string.Join("&", query)}";
        var result = await SendAsync(endpoint, username, password, cancellationToken);
        if (!result.Success)
            return Failure(result.Message);

        return new OnvifNetworkChangeResult
        {
            Succeeded = true,
            RebootNeeded = !useDhcp && !string.Equals(device.IpAddress, ipv4Address, StringComparison.OrdinalIgnoreCase),
            Message = "Configuración aceptada por el CGI VIVOTEK legacy."
        };
    }

    public async Task<OnvifNetworkChangeResult> SetHostnameAsync(
        DiscoveredDevice device,
        string username,
        string password,
        string hostname,
        CancellationToken cancellationToken = default)
    {
        hostname = hostname.Trim();
        if (string.IsNullOrWhiteSpace(hostname) || hostname.Length > 40)
            return Failure("El nombre de cámara no es válido para este firmware VIVOTEK.");

        var endpoint = $"http://{device.IpAddress.Trim()}/cgi-bin/admin/setparam.cgi?system_hostname={Uri.EscapeDataString(hostname)}&update=1";
        var result = await SendAsync(endpoint, username, password, cancellationToken);
        return result.Success
            ? new OnvifNetworkChangeResult { Succeeded = true, Message = "Nombre de cámara actualizado mediante CGI VIVOTEK." }
            : Failure(result.Message);
    }

    public async Task<OnvifNetworkChangeResult> RebootAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var endpoint = $"http://{device.IpAddress.Trim()}/cgi-bin/admin/setparam.cgi?system_reset=1&update=1";
        var result = await SendAsync(endpoint, username, password, cancellationToken);
        if (result.Success || IsExpectedDisconnectAfterSystemAction(result))
            return new OnvifNetworkChangeResult { Succeeded = true, Message = "La cámara aceptó la orden de reinicio mediante CGI VIVOTEK." };
        return Failure(result.Message);
    }

    public async Task<OnvifNetworkChangeResult> FactoryResetAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var endpoint = $"http://{device.IpAddress.Trim()}/cgi-bin/admin/setparam.cgi?system_restore=1&update=1";
        var result = await SendAsync(endpoint, username, password, cancellationToken);
        if (result.Success || IsExpectedDisconnectAfterSystemAction(result))
            return new OnvifNetworkChangeResult { Succeeded = true, RebootNeeded = true, Message = "La cámara aceptó el restablecimiento de fábrica mediante CGI VIVOTEK." };
        return Failure(result.Message);
    }

    public async Task<OnvifNetworkChangeResult> SetRootPasswordAsync(
        DiscoveredDevice device,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return Failure("La cámara no tiene una dirección IP válida.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            return Failure("La nueva contraseña debe tener al menos 4 caracteres.");

        var endpoint = $"http://{device.IpAddress.Trim()}/cgi-bin/admin/editaccount.cgi?method=edit&username=root&userpass={Uri.EscapeDataString(newPassword)}&privilege=admin";
        var result = await SendAsync(endpoint, "root", currentPassword ?? string.Empty, cancellationToken);
        return result.Success
            ? new OnvifNetworkChangeResult { Succeeded = true, Message = "La contraseña del usuario root fue aceptada por la cámara." }
            : Failure(result.Message);
    }

    private async Task<Dictionary<string, string>> GetParametersAsync(
        string ip,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var endpoint = $"http://{ip.Trim()}/cgi-bin/admin/getparam.cgi?network_ipaddress&network_subnet&network_router&network_resetip&network_dns1&network_dns2&system_hostname";
        var result = await SendAsync(endpoint, username, password, cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Body))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in result.Body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
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

    private async Task<(bool Success, string Message, string Body)> SendAsync(
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
        using var client = new HttpClient(handler) { Timeout = _timeout };
        client.DefaultRequestHeaders.ConnectionClose = true;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CameraInspector/1.0");

        try
        {
            using var response = await client.GetAsync(endpoint, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return (false, "HTTP 401 Unauthorized: la cámara exige credenciales administrativas para esta operación.", body);
            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", body);

            var bodyLower = body.ToLowerInvariant();
            if (bodyLower.Contains("unauthorized") || bodyLower.Contains("access denied") || bodyLower.Contains("permission denied"))
                return (false, "El CGI VIVOTEK rechazó la operación por permisos insuficientes.", body);
            if (bodyLower.Contains("error") && !bodyLower.Contains("no_error"))
                return (false, "El CGI VIVOTEK informó un error al procesar la operación.", body);

            return (true, "OK", body);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "Tiempo de espera agotado al comunicarse con el CGI VIVOTEK.", string.Empty);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"No se pudo conectar con el CGI VIVOTEK: {ex.Message}", string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"No se pudo comunicar con el CGI VIVOTEK: {ex.Message}", string.Empty);
        }
    }

    private static bool IsExpectedDisconnectAfterSystemAction((bool Success, string Message, string Body) result) =>
        !result.Success &&
        (result.Message.Contains("Tiempo de espera", StringComparison.OrdinalIgnoreCase)
         || result.Message.Contains("conexión", StringComparison.OrdinalIgnoreCase)
         || result.Message.Contains("conectar", StringComparison.OrdinalIgnoreCase));

    private static OnvifNetworkChangeResult Failure(string message) => new() { Succeeded = false, Message = message };

    private static string? GetValue(Dictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) ? value : null;

    private static bool? GetBoolean(Dictionary<string, string> values, string name)
    {
        var value = GetValue(values, name);
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => bool.TryParse(value, out var parsed) ? parsed : null
        };
    }

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
