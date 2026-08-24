using System.Security;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Implementación del Media Service ONVIF.
/// Separa la obtención de perfiles de la resolución de su URI RTSP para que las capas
/// superiores puedan inspeccionar capacidades sin abrir todavía el stream.
/// </summary>
public sealed class OnvifMediaService : IStreamUriResolver, IOnvifMediaService
{
    private const string GetProfilesBody = """
        <trt:GetProfiles xmlns:trt="http://www.onvif.org/ver10/media/wsdl"/>
        """;

    private readonly IOnvifDeviceService _deviceService;

    public OnvifMediaService(IOnvifDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    public async Task<IReadOnlyList<OnvifMediaProfile>> GetProfilesAsync(
        DiscoveredDevice device,
        string mediaServiceXAddr,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaServiceXAddr))
            return [];

        var security = BuildSecurity(username, password);
        var document = await OnvifSoapClient.PostAsync(
            mediaServiceXAddr,
            GetProfilesBody,
            security,
            cancellationToken);

        if (document is null)
            return [];

        return OnvifSoapClient.AllElements(document, "Profiles")
            .Select(ParseProfile)
            .Where(profile => profile is not null)
            .Select(profile => profile!)
            .ToList();
    }

    public async Task<string?> GetStreamUriAsync(
        string mediaServiceXAddr,
        string profileToken,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaServiceXAddr) || string.IsNullOrWhiteSpace(profileToken))
            return null;

        var escapedToken = SecurityElement.Escape(profileToken);
        var body = $"""
            <trt:GetStreamUri xmlns:trt="http://www.onvif.org/ver10/media/wsdl">
              <trt:StreamSetup>
                <tt:Stream xmlns:tt="http://www.onvif.org/ver10/schema">RTP-Unicast</tt:Stream>
                <tt:Transport xmlns:tt="http://www.onvif.org/ver10/schema">
                  <tt:Protocol>RTSP</tt:Protocol>
                </tt:Transport>
              </trt:StreamSetup>
              <trt:ProfileToken>{escapedToken}</trt:ProfileToken>
            </trt:GetStreamUri>
            """;

        var document = await OnvifSoapClient.PostAsync(
            mediaServiceXAddr,
            body,
            BuildSecurity(username, password),
            cancellationToken);

        return document is null
            ? null
            : OnvifSoapClient.FirstValue(document, "Uri")?.Trim();
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

        var mediaXAddr = capabilities?.MediaServiceXAddr;
        if (string.IsNullOrWhiteSpace(mediaXAddr))
            return null;

        var profiles = await GetProfilesAsync(
            device,
            mediaXAddr,
            username,
            password,
            cancellationToken);

        var mainProfile = profiles
            .Select((profile, index) => new { profile, index })
            .OrderByDescending(item => item.profile.ResolutionPixels)
            .ThenBy(item => item.index)
            .FirstOrDefault()?.profile;

        if (mainProfile is null)
            return null;

        var uri = await GetStreamUriAsync(
            mediaXAddr,
            mainProfile.Token,
            username,
            password,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(uri))
            return null;

        return new CameraStreamInfo
        {
            RtspUri = uri,
            ProfileToken = mainProfile.Token,
            ProfileName = mainProfile.Name,
            IsMainStream = true
        };
    }

    private static OnvifMediaProfile? ParseProfile(XElement profile)
    {
        var token = profile.Attribute("token")?.Value;
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var videoEncoder = profile
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "VideoEncoderConfiguration");

        var resolution = videoEncoder?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Resolution");

        var width = ParseInt(resolution, "Width");
        var height = ParseInt(resolution, "Height");
        var frameRate = ParseInt(videoEncoder, "FrameRateLimit");
        var encoding = videoEncoder?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Encoding")?
            .Value;

        return new OnvifMediaProfile
        {
            Token = token.Trim(),
            Name = profile.Elements().FirstOrDefault(element => element.Name.LocalName == "Name")?.Value.Trim(),
            Width = width,
            Height = height,
            Encoding = string.IsNullOrWhiteSpace(encoding) ? null : encoding.Trim(),
            FrameRate = frameRate
        };
    }

    private static int? ParseInt(XElement? parent, string elementName)
    {
        var value = parent?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == elementName)?
            .Value;

        return int.TryParse(value, out var result) ? result : null;
    }

    private static string? BuildSecurity(string? username, string? password) =>
        (username, password) is (not null, not null)
            ? WsSecurityHeaderBuilder.Build(username!, password!)
            : null;
}
