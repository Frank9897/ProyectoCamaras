using System.Security;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Implementación del Media Service ONVIF.
/// Consulta perfiles de video, identifica sus capacidades y resuelve las URI RTSP.
/// Para cámaras legacy sin ONVIF (VIVOTEK, DAHUA, HIKVISION) arma además la URI RTSP
/// clásica de cada fabricante como respaldo cuando GetStreamUri por ONVIF falla.
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
        // Las legacy como VIVOTEK IP71xx, DAHUA o HIKVISION sin ONVIF no implementan
        // GetStreamUri, pero sí RTSP clásico: se prueba primero si el fabricante es
        // conocido y la cámara no fue marcada como ONVIF, para evitar un intento SOAP
        // innecesario que de todas formas va a fallar.
        if (!device.OnvifSupported)
        {
            var earlyLegacyUri = BuildLegacyFallbackIfPossible(device, isMainStream);
            if (earlyLegacyUri is not null)
                return earlyLegacyUri;
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
                return BuildLegacyFallbackIfPossible(device, isMainStream);

            var profiles = await GetProfilesAsync(
                device,
                mediaXAddr,
                username,
                password,
                cancellationToken);

            if (profiles.Count == 0)
                return BuildLegacyFallbackIfPossible(device, isMainStream);

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
                return BuildLegacyFallbackIfPossible(device, isMainStream);

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
            return BuildLegacyFallbackIfPossible(device, isMainStream);
        }
    }

    // FIX: antes exigía "manufacturer contiene VIVOTEK Y (modelo IP7133 O no-ONVIF)".
    // El AND con el manufacturer dejaba afuera cualquier VIVOTEK detectada sin ese campo
    // bien etiquetado. Ahora es consistente con el resto de la app: manufacturer O modelo
    // de la familia IP71xx alcanza para tratarla como legacy.
    private static bool IsLegacyVivotek(DiscoveredDevice device)
    {
        var manufacturer = device.Manufacturer ?? string.Empty;
        var model = device.Model ?? string.Empty;
        return manufacturer.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase)
            || model.Contains("IP71", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyDahua(DiscoveredDevice device)
    {
        var manufacturer = device.Manufacturer ?? string.Empty;
        return manufacturer.Contains("Dahua", StringComparison.OrdinalIgnoreCase)
            || manufacturer.Contains("Amcrest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyHikvision(DiscoveredDevice device)
    {
        var manufacturer = device.Manufacturer ?? string.Empty;
        var model = device.Model ?? string.Empty;
        return manufacturer.Contains("Hikvision", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("DS-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Si ONVIF falla (o la cámara no lo soporta), se arma la URI RTSP clásica del
    /// fabricante detectado. Antes esto solo existía para VIVOTEK: cualquier DAHUA o
    /// HIKVISION vieja sin ONVIF se quedaba directamente sin video, sin ningún intento
    /// adicional. Devuelve null si el fabricante no es ninguno de los tres reconocidos
    /// (no hay una ruta RTSP "genérica" real: cada fabricante usa su propio access name).
    /// </summary>
    private static CameraStreamInfo? BuildLegacyFallbackIfPossible(
        DiscoveredDevice device,
        bool isMainStream)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        if (IsLegacyVivotek(device))
            return BuildLegacyVivotekRtspUri(device, isMainStream);

        if (IsLegacyDahua(device))
            return BuildLegacyDahuaRtspUri(device, isMainStream);

        if (IsLegacyHikvision(device))
            return BuildLegacyHikvisionRtspUri(device, isMainStream);

        return null;
    }

    private static int NormalizedRtspPort(DiscoveredDevice device)
    {
        var port = device.RtspPort.GetValueOrDefault(554);
        return port is > 0 and <= 65535 ? port : 554;
    }

    private static CameraStreamInfo? BuildLegacyVivotekRtspUri(
        DiscoveredDevice device,
        bool isMainStream)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        var port = NormalizedRtspPort(device);

        // IP71xx (7122/7133/7134...) documentan live.sdp para stream 1 y live2.sdp para stream 2.
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

    private static CameraStreamInfo? BuildLegacyDahuaRtspUri(
        DiscoveredDevice device,
        bool isMainStream)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        var port = NormalizedRtspPort(device);

        // Convención DAHUA (y OEM/Amcrest): canal 1, subtype=0 principal, subtype=1 secundario.
        var subtype = isMainStream ? 0 : 1;
        return new CameraStreamInfo
        {
            RtspUri = $"rtsp://{device.IpAddress.Trim()}:{port}/cam/realmonitor?channel=1&subtype={subtype}",
            ProfileToken = isMainStream ? "dahua-legacy-main" : "dahua-legacy-sub",
            ProfileName = isMainStream ? "DAHUA Legacy Stream principal" : "DAHUA Legacy Stream secundario",
            Width = null,
            Height = null,
            Encoding = "H.264/H.265 / legacy RTSP",
            FrameRate = null,
            IsMainStream = isMainStream
        };
    }

    private static CameraStreamInfo? BuildLegacyHikvisionRtspUri(
        DiscoveredDevice device,
        bool isMainStream)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        var port = NormalizedRtspPort(device);

        // Convención ISAPI/HIKVISION: canal*100 + tipo de stream. Canal 1 principal = 101,
        // canal 1 secundario = 102. Esta app administra siempre el canal 1 (cámara única).
        var channelSuffix = isMainStream ? "101" : "102";
        return new CameraStreamInfo
        {
            RtspUri = $"rtsp://{device.IpAddress.Trim()}:{port}/Streaming/Channels/{channelSuffix}",
            ProfileToken = isMainStream ? "hikvision-legacy-main" : "hikvision-legacy-sub",
            ProfileName = isMainStream ? "HIKVISION Legacy Stream principal" : "HIKVISION Legacy Stream secundario",
            Width = null,
            Height = null,
            Encoding = "H.264/H.265 / legacy RTSP",
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
