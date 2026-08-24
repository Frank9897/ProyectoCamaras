using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Implementación real de la Capa 5 para obtener la URL de stream: primero pide los
/// perfiles de media disponibles (GetProfiles), elige el principal, y para ese perfil
/// pide la URL RTSP real (GetStreamUri). Ambas llamadas van contra el mismo endpoint
/// /onvif/media_service que expone la Capa 4 al detectar el dispositivo como ONVIF.
/// </summary>
public sealed class OnvifMediaService : IStreamUriResolver
{
    private const string GetProfilesBody = """
        <trt:GetProfiles xmlns:trt="http://www.onvif.org/ver10/media/wsdl"/>
        """;

    public async Task<CameraStreamInfo?> GetMainStreamUriAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var endpoint = $"http://{device.IpAddress}/onvif/media_service";
        var security = (username, password) is (not null, not null)
            ? WsSecurityHeaderBuilder.Build(username!, password!)
            : null;

        var profilesDoc = await OnvifSoapClient.PostAsync(endpoint, GetProfilesBody, security, cancellationToken);
        if (profilesDoc is null)
            return null; // sin auth, endpoint distinto, o dispositivo no soporta Media Service

        var profileTokens = OnvifSoapClient.AllElements(profilesDoc, "Profiles")
            .Select(p => p.Attribute("token")?.Value)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (profileTokens.Count == 0)
            return null;

        // Fase 1: nos quedamos con el primer perfil (suele ser el stream principal en la
        // mayoría de los firmwares). Elegir por resolución real queda para cuando parseemos
        // VideoEncoderConfiguration dentro de cada <Profiles>, en un próximo ajuste.
        var mainToken = profileTokens[0]!;

        var getStreamUriBody = $"""
            <trt:GetStreamUri xmlns:trt="http://www.onvif.org/ver10/media/wsdl">
              <trt:StreamSetup>
                <tt:Stream xmlns:tt="http://www.onvif.org/ver10/schema">RTP-Unicast</tt:Stream>
                <tt:Transport xmlns:tt="http://www.onvif.org/ver10/schema">
                  <tt:Protocol>RTSP</tt:Protocol>
                </tt:Transport>
              </trt:StreamSetup>
              <trt:ProfileToken>{mainToken}</trt:ProfileToken>
            </trt:GetStreamUri>
            """;

        // El nonce/created deben regenerarse por request (WS-Security no permite reusar un digest).
        var security2 = (username, password) is (not null, not null)
            ? WsSecurityHeaderBuilder.Build(username!, password!)
            : null;

        var streamDoc = await OnvifSoapClient.PostAsync(endpoint, getStreamUriBody, security2, cancellationToken);
        if (streamDoc is null)
            return null;

        var uri = OnvifSoapClient.FirstValue(streamDoc, "Uri");
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        return new CameraStreamInfo
        {
            RtspUri = uri.Trim(),
            ProfileToken = mainToken,
            IsMainStream = true
        };
    }
}
