using System.Net;
using System.Text.RegularExpressions;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Implementa las operaciones de administración de red y sistema definidas por ONVIF.
/// </summary>
public sealed class OnvifNetworkConfigurationService : IOnvifNetworkConfigurationService
{
    public async Task<OnvifNetworkChangeResult> SetIPv4Async(
        DiscoveredDevice device,
        string username,
        string password,
        string interfaceToken,
        bool useDhcp,
        string? ipv4Address,
        int? prefixLength,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(interfaceToken))
            return Failure("La cámara no proporcionó un token de interfaz de red válido.");

        if (!useDhcp)
        {
            if (!IPAddress.TryParse(ipv4Address, out var parsedIp) || parsedIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return Failure("La dirección IPv4 no es válida.");
            if (prefixLength is null or < 0 or > 32)
                return Failure("El prefijo IPv4 debe estar entre 0 y 32.");
        }

        var endpoint = ResolveDeviceServiceEndpoint(device);
        if (endpoint is null)
            return Failure("No se pudo resolver el Device Service ONVIF.");

        var validatedPrefix = prefixLength.GetValueOrDefault();
        var security = WsSecurityHeaderBuilder.Build(username, password);
        var ipv4Configuration = useDhcp
            ? """
              <tt:Enabled>true</tt:Enabled>
              <tt:Config><tt:DHCP>true</tt:DHCP></tt:Config>
              """
            : $"""
              <tt:Enabled>true</tt:Enabled>
              <tt:Manual>
                <tt:Address>{SecurityEscape(ipv4Address!)}</tt:Address>
                <tt:PrefixLength>{validatedPrefix}</tt:PrefixLength>
              </tt:Manual>
              <tt:DHCP>false</tt:DHCP>
              """;

        var body = $"""
            <tds:SetNetworkInterfaces>
              <tds:InterfaceToken>{SecurityEscape(interfaceToken)}</tds:InterfaceToken>
              <tds:NetworkInterface><tt:IPv4>{ipv4Configuration}</tt:IPv4></tds:NetworkInterface>
            </tds:SetNetworkInterfaces>
            """;

        var response = await OnvifSoapClient.PostAsync(endpoint, body, security, cancellationToken);
        if (response is null)
            return Failure("La cámara rechazó la modificación de la interfaz de red o no respondió.");

        var rebootNeeded = bool.TryParse(OnvifSoapClient.FirstValue(response, "RebootNeeded"), out var reboot) && reboot;
        return new OnvifNetworkChangeResult
        {
            Succeeded = true,
            RebootNeeded = rebootNeeded,
            Message = rebootNeeded
                ? "La cámara aceptó el cambio y reportó que necesita reinicio para activarlo."
                : "La cámara aceptó el cambio de configuración IPv4."
        };
    }

    public async Task<OnvifNetworkChangeResult> SetDefaultGatewayAsync(
        DiscoveredDevice device,
        string username,
        string password,
        string? gatewayAddress,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveDeviceServiceEndpoint(device);
        if (endpoint is null)
            return Failure("No se pudo resolver el Device Service ONVIF.");

        if (!string.IsNullOrWhiteSpace(gatewayAddress)
            && (!IPAddress.TryParse(gatewayAddress, out var parsedGateway)
                || parsedGateway.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork))
        {
            return Failure("La dirección de gateway IPv4 no es válida.");
        }

        var gatewayXml = string.IsNullOrWhiteSpace(gatewayAddress)
            ? string.Empty
            : $"<tt:IPv4Address>{SecurityEscape(gatewayAddress.Trim())}</tt:IPv4Address>";

        var response = await OnvifSoapClient.PostAsync(
            endpoint,
            $"<tds:SetNetworkDefaultGateway>{gatewayXml}</tds:SetNetworkDefaultGateway>",
            WsSecurityHeaderBuilder.Build(username, password),
            cancellationToken);

        return response is null
            ? Failure("La cámara rechazó la modificación del gateway o no respondió.")
            : new OnvifNetworkChangeResult
            {
                Succeeded = true,
                RebootNeeded = false,
                Message = string.IsNullOrWhiteSpace(gatewayAddress)
                    ? "El gateway IPv4 fue limpiado correctamente."
                    : "El gateway IPv4 fue actualizado correctamente."
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
        if (string.IsNullOrWhiteSpace(hostname) || hostname.Length > 63 ||
            !Regex.IsMatch(hostname, "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$"))
            return Failure("El nombre de cámara no es válido. Use letras, números y guiones, entre 1 y 63 caracteres.");

        var endpoint = ResolveDeviceServiceEndpoint(device);
        if (endpoint is null)
            return Failure("No se pudo resolver el Device Service ONVIF.");

        var response = await OnvifSoapClient.PostAsync(
            endpoint,
            $"<tds:SetHostname><tds:Name>{SecurityEscape(hostname)}</tds:Name></tds:SetHostname>",
            WsSecurityHeaderBuilder.Build(username, password),
            cancellationToken);

        return response is null
            ? Failure("La cámara rechazó el cambio de nombre o no respondió.")
            : new OnvifNetworkChangeResult { Succeeded = true, Message = "Nombre de cámara actualizado correctamente." };
    }

    public async Task<OnvifNetworkChangeResult> RebootAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveDeviceServiceEndpoint(device);
        if (endpoint is null)
            return Failure("No se pudo resolver el Device Service ONVIF.");

        var response = await OnvifSoapClient.PostAsync(
            endpoint,
            "<tds:SystemReboot/>",
            WsSecurityHeaderBuilder.Build(username, password),
            cancellationToken);

        return response is null
            ? Failure("La cámara no confirmó el reinicio.")
            : new OnvifNetworkChangeResult { Succeeded = true, RebootNeeded = false, Message = "La cámara aceptó el reinicio." };
    }

    public async Task<OnvifNetworkChangeResult> FactoryResetAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveDeviceServiceEndpoint(device);
        if (endpoint is null)
            return Failure("No se pudo resolver el Device Service ONVIF.");

        var response = await OnvifSoapClient.PostAsync(
            endpoint,
            "<tds:SetSystemFactoryDefault><tds:FactoryDefault>Hard</tds:FactoryDefault></tds:SetSystemFactoryDefault>",
            WsSecurityHeaderBuilder.Build(username, password),
            cancellationToken);

        return response is null
            ? Failure("La cámara rechazó el restablecimiento de fábrica o no respondió.")
            : new OnvifNetworkChangeResult { Succeeded = true, RebootNeeded = true, Message = "La cámara aceptó el restablecimiento de fábrica. Los valores de red y acceso pueden volver a sus valores iniciales." };
    }

    /// <summary>
    /// Lee el hostname sin requerir otra interfaz pública en el servicio de dispositivo.
    /// </summary>
    public async Task<string?> GetHostnameAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveDeviceServiceEndpoint(device);
        if (endpoint is null)
            return null;

        var response = await OnvifSoapClient.PostAsync(
            endpoint,
            "<tds:GetHostname/>",
            WsSecurityHeaderBuilder.Build(username, password),
            cancellationToken);
        return string.IsNullOrWhiteSpace(response)
            ? null
            : OnvifSoapClient.FirstValue(response, "Name");
    }

    private static string? ResolveDeviceServiceEndpoint(DiscoveredDevice device)
    {
        if (!string.IsNullOrWhiteSpace(device.OnvifDeviceServiceXAddr)
            && Uri.TryCreate(device.OnvifDeviceServiceXAddr, UriKind.Absolute, out var discoveredUri)
            && (string.Equals(discoveredUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(discoveredUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return discoveredUri.ToString();

        return string.IsNullOrWhiteSpace(device.IpAddress) ? null : $"http://{device.IpAddress}/onvif/device_service";
    }

    private static OnvifNetworkChangeResult Failure(string message) => new() { Succeeded = false, Message = message };

    private static string SecurityEscape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
