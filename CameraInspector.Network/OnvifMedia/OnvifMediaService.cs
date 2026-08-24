using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Obtiene la URL RTSP real desde ONVIF. Primero consulta GetCapabilities para obtener
/// la URL anunciada por el firmware y luego usa Media Service (GetProfiles + GetStreamUri).
/// </summary>
public sealed class OnvifMediaService : IStreamUriResolver
{
    private const string GetProfilesBody = """
        <trt:GetProfiles xmlns:trt="http://www.onvif.org/ver10/media/wsdl"/>
        """;

    private readonly IOnvifDeviceService _deviceService;

    public OnvifMediaService(IOnvifDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    public async Task<CameraStreamInfo?> GetMainStreamUriAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var capabilities = await _deviceService.GetCapabilitiesAsync(
            device,
            username,
            password,
            cancellationToken);

        var endpoint = capabilities?.MediaServiceXAddr;
        if (string.IsNullOrWhiteSpace(endpoint))
            return null;

        var security = (username, password) is (not null, not null)
            ? WsSecurityHeaderBuilder.Build(username!, password!)
            : null;

        var profilesDoc = await OnvifSoapClient.PostAsync(
            endpoint,
            GetProfilesBody,
            security,
            cancellationToken);

        if (profilesDoc is null)
            return null;

        var profiles = OnvifSoapClient.AllElements(profilesDoc, "Profiles")
            .Select(profile =>
            {
                var token = profile.Attribute("token")?.Value;
                var width = profile
                    .Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "Resolution")?
                    .Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "Width")?.Value;
                var height = profile
                    .Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "Resolution")?
                    .Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "Height")?.Value;

                _ = int.TryParse(width, out var parsedWidth);
                _ = int.TryParse(height, out var parsedHeight);

                return new
                {
                    Element = profile,
                    Token = token,
                    Width = parsedWidth,
                    Height = parsedHeight
                };
            })
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Token))
            .ToList();

        if (profiles.Count == 0)
            return null;

        // Preferimos el perfil con mayor resolución. Si el firmware no informa resolución,
        // todos tendrán 0x0 y se conserva el orden anunciado por la cámara.
        var mainProfile = profiles
            .OrderByDescending(profile => (long)profile.Width * profile.Height)
            .First();

        var getStreamUriBody = $"""
            <trt:GetStreamUri xmlns:trt="http://www.onvif.org/ver10/media/wsdl">
              <trt:StreamSetup>
                <tt:Stream xmlns:tt="http://www.onvif.org/ver10/schema">RTP-Unicast</tt:Stream>
                <tt:Transport xmlns:tt="http://www.onvif.org/ver10/schema">
                  <tt:Protocol>RTSP</tt:Protocol>
                </tt:Transport>
              </trt:StreamSetup>
              <trt:ProfileToken>{System.Security.SecurityElement.Escape(mainProfile.Token)} </trt:ProfileToken>
            </trt:GetStreamUri>
            """.Replace($"{System.Environment.NewLine}              <trt:ProfileToken>{System.Security.SecurityElement.Escape(mainProfile.Token)} </trt:ProfileToken>", $"{System.Environment.NewLine}              <trt:ProfileToken>{System.Security.SecurityElement.Escape(mainProfile.Token)}</trt:ProfileToken>");

        var streamDoc = await OnvifSoapClient.PostAsync(
            endpoint,
            getStreamUriBody,
            security,
            cancellationToken);

        if (streamDoc is null)
            return null;

        var uri = OnvifSoapClient.FirstValue(streamDoc, "Uri");
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        return new CameraStreamInfo
        {
            RtspUri = uri.Trim(),
            ProfileToken = mainProfile.Token!,
            IsMainStream = true
        };
    }
}
