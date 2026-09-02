using System.Security;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Implementación del Media Service ONVIF.
/// Consulta perfiles de video, identifica sus capacidades y resuelve las URI RTSP.
/// Para VIVOTEK antiguas sin ONVIF utiliza además sus access names RTSP clásicos.
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
                <tt:Stream xmlns:tt="http://www.onvif.org/ver10/schema">RTP-Unicast</tt>
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

    public Task<CameraStreamInfo?> GetMainStreamUriAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return GetBestStreamUriAsync(device, true, username, password, cancellationToken);
    }

    public Task<CameraStreamInfo?> GetSubStreamUriAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return GetBestStreamUriAsync(device, false, username, password, cancellationToken);
    }

    private async Task<CameraStreamInfo?> GetBestStreamUriAsync(
        DiscoveredDevice device,
        bool isMainStream,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        // Las VIVOTEK legacy como IP7133 no implementan ONVIF, pero sí RTSP clásico.
        if (IsLegacyVivotek(device))
        {
            var legacyUri = BuildLegacyVivotekRtspUri(device, isMainStream);
            if (legacyUri is not null)
                return legacyUri;
        }

        try
        {
            var capabilities = await _deviceService.GetCapabilitiesAsync(
                device,
                username,
                password,
                cancellationToken);

            var mediaXAddr = capabilities?.MediaServiceXAddr;
            if (string.IsNullOrWhiteSpace(mediaXAddr))
                return BuildVivotekFallbackIfPossible(device, isMainStream);

            var profiles = await GetProfilesAsync(
                device,
                mediaXAddr,
                username,
                password,
                cancellationToken);

            if (profiles.Count == 0)
                return BuildVivotekFallbackIfPossible(device, isMainStream);

            var orderedProfiles = profiles
                .OrderBy(profile => profile.ResolutionPixels)
                .ThenBy(profile => profile.Name ?? profile.Token)
                .ToList();

            var selectedProfile = isMainStream
                ? orderedProfiles[^1]
                : orderedProfiles[0];

            var uri = await GetStreamUriAsync(
                mediaXAddr,
                selectedProfile.Token,
                username,
                password,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(uri))
                return BuildVivotekFallbackIfPossible(device, isMainStream);

            return new CameraStreamInfo
            {
                RtspUri = uri,
                ProfileToken = selectedProfile.Token,
                ProfileName = selectedProfile.Name,
                Width = selectedProfile.Width,
                Height = selectedProfile.Height,
                Encoding = selectedProfile.Encoding,
                FrameRate = selectedProfile.FrameRate,
                IsMainStream = isMainStream
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return BuildVivotekFallbackIfPossible(device, isMainStream);
        }
    }

    private static bool IsLegacyVivotek(DiscoveredDevice device)
    {
        var manufacturer = device.Manufacturer ?? string.Empty;
        var model = device.Model ?? string.Empty;
        return manufacturer.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase)
            && (model.Contains("IP7133", StringComparison.OrdinalIgnoreCase)
                || !device.OnvifSupported);
    }

    private static CameraStreamInfo? BuildVivotekFallbackIfPossible(
        DiscoveredDevice device,
        bool isMainStream)
    {
        return IsLegacyVivotek(device)
            ? BuildLegacyVivotekRtspUri(device, isMainStream)
            : null;
    }

    private static CameraStreamInfo? BuildLegacyVivotekRtspUri(
        DiscoveredDevice device,
        bool isMainStream)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        var port = device.RtspPort.GetValueOrDefault(554);
        if (port <= 0 || port > 65535)
            port = 554;

        // IP7133/IP7134 documentan live.sdp para stream 1 y live2.sdp para stream 2.
        var accessName = isMainStream ? "live.sdp" : "live2.sdp";
        return new CameraStreamInfo
        {
            RtspUri = $"rtsp://{device.IpAddress.Trim()}:{port}/{accessName}",
            ProfileToken = isMainStream ? "vivotek-legacy-main" : "vivotek-legacy-sub",
            ProfileName = isMainStream ? "VIVOTEK Legacy Stream 1" : "VIVOTEK Legacy Stream 2",
            Width = null,
            Height = null,
            Encoding = "MPEG-4 / legacy RTSP",
            FrameRate = null,
            IsMainStream = isMainStream
        };
    }

    private static OnvifMediaProfile? ParseProfile(XElement profile)
    {
        var token = profile.Attribute("token")?.Value;
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var videoSourceConfiguration = profile
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "VideoSourceConfiguration");

        var videoSourceToken = videoSourceConfiguration?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "SourceToken")?
            .Value;

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
            Name = profile.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Name")?
                .Value
                .Trim(),
            VideoSourceToken = string.IsNullOrWhiteSpace(videoSourceToken) ? null : videoSourceToken.Trim(),
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
