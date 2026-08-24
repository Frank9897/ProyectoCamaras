using System.Globalization;
using System.Text;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Implementación genérica del PTZ ONVIF mediante SOAP.
/// No conoce ningún endpoint propietario: utiliza exclusivamente los XAddr anunciados por la cámara.
/// </summary>
public sealed class OnvifPtzService : IOnvifPtzService
{
    private readonly HttpClient _httpClient;
    private readonly IOnvifMediaService _mediaService;

    public OnvifPtzService(HttpClient httpClient, IOnvifMediaService mediaService)
    {
        // _httpClient reutiliza conexiones HTTP para las llamadas SOAP.
        _httpClient = httpClient;
        // _mediaService permite resolver un ProfileToken real antes de ejecutar PTZ.
        _mediaService = mediaService;
    }

    public async Task<bool> ContinuousMoveAsync(
        DiscoveredDevice device,
        OnvifPtzMoveRequest request,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        // ptzEndpoint es el XAddr que el dispositivo anunció para su servicio PTZ.
        var ptzEndpoint = ResolvePtzEndpoint(device);
        if (ptzEndpoint is null || string.IsNullOrWhiteSpace(device.OnvifMediaServiceXAddr))
            return false;

        // profileToken identifica el perfil de video al que se aplicará el movimiento.
        var profileToken = await ResolveFirstProfileTokenAsync(
            device,
            device.OnvifMediaServiceXAddr,
            username,
            password,
            cancellationToken);

        if (profileToken is null)
            return false;

        // Los valores se limitan para impedir que una UI defectuosa envíe velocidades fuera del rango normalizado.
        var pan = Math.Clamp(request.Pan, -1f, 1f);
        var tilt = Math.Clamp(request.Tilt, -1f, 1f);
        var zoom = Math.Clamp(request.Zoom, -1f, 1f);

        var body = $"""
                   <tptz:ContinuousMove>
                     <tptz:ProfileToken>{EscapeXml(profileToken)}</tptz:ProfileToken>
                     <tptz:Velocity>
                       <tt:PanTilt x="{pan.ToString(CultureInfo.InvariantCulture)}" y="{tilt.ToString(CultureInfo.InvariantCulture)}" />
                       <tt:Zoom x="{zoom.ToString(CultureInfo.InvariantCulture)}" />
                     </tptz:Velocity>
                   </tptz:ContinuousMove>
                   """;

        var security = BuildSecurity(username, password);
        return await SendSoapAsync(
            ptzEndpoint,
            "http://www.onvif.org/ver20/ptz/wsdl/ContinuousMove",
            body,
            security,
            cancellationToken);
    }

    public async Task<bool> StopAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        // ptzEndpoint es la URL donde la cámara espera las operaciones PTZ.
        var ptzEndpoint = ResolvePtzEndpoint(device);
        if (ptzEndpoint is null || string.IsNullOrWhiteSpace(device.OnvifMediaServiceXAddr))
            return false;

        // profileToken identifica el perfil que actualmente controla el movimiento.
        var profileToken = await ResolveFirstProfileTokenAsync(
            device,
            device.OnvifMediaServiceXAddr,
            username,
            password,
            cancellationToken);

        if (profileToken is null)
            return false;

        var body = $"""
                   <tptz:Stop>
                     <tptz:ProfileToken>{EscapeXml(profileToken)}</tptz:ProfileToken>
                     <tptz:PanTilt>true</tptz:PanTilt>
                     <tptz:Zoom>true</tptz:Zoom>
                   </tptz:Stop>
                   """;

        var security = BuildSecurity(username, password);
        return await SendSoapAsync(
            ptzEndpoint,
            "http://www.onvif.org/ver20/ptz/wsdl/Stop",
            body,
            security,
            cancellationToken);
    }

    /// <summary>
    /// Obtiene un token de perfil real. Preferimos el perfil de mayor resolución porque normalmente
    /// corresponde al canal principal de video de la cámara.
    /// </summary>
    private async Task<string?> ResolveFirstProfileTokenAsync(
        DiscoveredDevice device,
        string mediaServiceXAddr,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var profiles = await _mediaService.GetProfilesAsync(
            device,
            mediaServiceXAddr,
            username,
            password,
            cancellationToken);

        return profiles
            .OrderByDescending(profile => profile.ResolutionPixels)
            .Select(profile => profile.Token)
            .FirstOrDefault(token => !string.IsNullOrWhiteSpace(token));
    }

    private static string? ResolvePtzEndpoint(DiscoveredDevice device)
    {
        // endpoint es el XAddr anunciado por GetCapabilities/WS-Discovery.
        if (string.IsNullOrWhiteSpace(device.OnvifPtzServiceXAddr))
            return null;

        if (!Uri.TryCreate(device.OnvifPtzServiceXAddr, UriKind.Absolute, out var uri))
            return null;

        // Solo se permiten HTTP/HTTPS porque el transporte del SOAP PTZ usa HttpClient.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.ToString();
    }

    private static string? BuildSecurity(string? username, string? password) =>
        username is not null && password is not null
            ? WsSecurityHeaderBuilder.Build(username, password)
            : null;

    private async Task<bool> SendSoapAsync(
        string endpoint,
        string action,
        string body,
        string? security,
        CancellationToken cancellationToken)
    {
        // envelope contiene el sobre SOAP completo con los namespaces necesarios para PTZ y tipos ONVIF.
        var envelope = $"""
                      <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                                  xmlns:tptz="http://www.onvif.org/ver20/ptz/wsdl"
                                  xmlns:tt="http://www.onvif.org/ver10/schema">
                        <s:Header>{security ?? string.Empty}</s:Header>
                        <s:Body>{body}</s:Body>
                      </s:Envelope>
                      """;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("SOAPAction", action);
        request.Content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private static string EscapeXml(string value) =>
        new XElement("Value", value).Value;
}
