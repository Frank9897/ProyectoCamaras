using System.Security;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Implementación del Media Service ONVIF.
/// Consulta perfiles de video, identifica sus capacidades y resuelve las URI RTSP.
/// </summary>
public sealed class OnvifMediaService : IStreamUriResolver, IOnvifMediaService
{
    /// <summary>Cuerpo SOAP utilizado para obtener todos los perfiles disponibles.</summary>
    private const string GetProfilesBody = """
        <trt:GetProfiles xmlns:trt="http://www.onvif.org/ver10/media/wsdl"/>
        """;

    /// <summary>Servicio Device utilizado para obtener el Media XAddr real cuando sea necesario.</summary>
    private readonly IOnvifDeviceService _deviceService;

    public OnvifMediaService(IOnvifDeviceService deviceService)
    {
        // _deviceService permite resolver capacidades sin asumir rutas fijas del fabricante.
        _deviceService = deviceService;
    }

    public async Task<IReadOnlyList<OnvifMediaProfile>> GetProfilesAsync(
        DiscoveredDevice device,
        string mediaServiceXAddr,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        // Si no existe un Media XAddr no podemos consultar perfiles.
        if (string.IsNullOrWhiteSpace(mediaServiceXAddr))
            return [];

        // security contiene WS-Security cuando la cámara exige autenticación.
        var security = BuildSecurity(username, password);

        // document contiene la respuesta SOAP de GetProfiles.
        var document = await OnvifSoapClient.PostAsync(
            mediaServiceXAddr,
            GetProfilesBody,
            security,
            cancellationToken);

        // Una respuesta vacía o inválida significa que no pudimos obtener perfiles.
        if (document is null)
            return [];

        // Cada elemento Profiles representa un perfil independiente de video.
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
        // El Media XAddr y el token son obligatorios para pedir una URI de stream.
        if (string.IsNullOrWhiteSpace(mediaServiceXAddr) || string.IsNullOrWhiteSpace(profileToken))
            return null;

        // Escapamos el token porque se inserta dentro del XML SOAP.
        var escapedToken = SecurityElement.Escape(profileToken);

        // body solicita RTSP unicast para el perfil indicado.
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

        // document contiene la respuesta con la URI RTSP generada por la cámara.
        var document = await OnvifSoapClient.PostAsync(
            mediaServiceXAddr,
            body,
            BuildSecurity(username, password),
            cancellationToken);

        // Uri es el endpoint RTSP real que podremos entregar posteriormente al reproductor.
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
        return await GetBestStreamUriAsync(
            device,
            isMainStream: true,
            username,
            password,
            cancellationToken);
    }

    /// <summary>
    /// Obtiene el stream secundario seleccionando el perfil de menor resolución disponible.
    /// </summary>
    public async Task<CameraStreamInfo?> GetSubStreamUriAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return await GetBestStreamUriAsync(
            device,
            isMainStream: false,
            username,
            password,
            cancellationToken);
    }

    /// <summary>
    /// Selecciona un perfil de mayor o menor resolución según el stream solicitado.
    /// Se mantiene centralizado para que Main y Sub utilicen exactamente la misma lógica.
    /// </summary>
    private async Task<CameraStreamInfo?> GetBestStreamUriAsync(
        DiscoveredDevice device,
        bool isMainStream,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        // capabilities contiene los XAddr publicados por el Device Service.
        var capabilities = await _deviceService.GetCapabilitiesAsync(
            device,
            username,
            password,
            cancellationToken);

        var mediaXAddr = capabilities?.MediaServiceXAddr;
        if (string.IsNullOrWhiteSpace(mediaXAddr))
            return null;

        // profiles contiene todos los perfiles de video que la cámara permite consultar.
        var profiles = await GetProfilesAsync(
            device,
            mediaXAddr,
            username,
            password,
            cancellationToken);

        if (profiles.Count == 0)
            return null;

        // orderedProfiles ordena por resolución para poder identificar Main y Sub sin depender
        // del orden arbitrario en el que el firmware devuelve los perfiles.
        var orderedProfiles = profiles
            .OrderBy(profile => profile.ResolutionPixels)
            .ThenBy(profile => profile.Name ?? profile.Token)
            .ToList();

        // El stream principal utiliza el perfil de mayor resolución disponible.
        // El secundario utiliza el de menor resolución disponible cuando existen varios perfiles.
        var selectedProfile = isMainStream
            ? orderedProfiles[^1]
            : orderedProfiles[0];

        // Resolvemos la URI RTSP real del perfil elegido.
        var uri = await GetStreamUriAsync(
            mediaXAddr,
            selectedProfile.Token,
            username,
            password,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(uri))
            return null;

        // El resultado conserva tanto la URI como las capacidades del perfil para la UI y el reproductor.
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

    /// <summary>
    /// Convierte el XML de un perfil ONVIF al modelo Core utilizado por el resto de la aplicación.
    /// </summary>
    private static OnvifMediaProfile? ParseProfile(XElement profile)
    {
        // token identifica de manera única el perfil dentro de Media Service.
        var token = profile.Attribute("token")?.Value;
        if (string.IsNullOrWhiteSpace(token))
            return null;

        // videoSourceConfiguration identifica la fuente de imagen asociada al perfil.
        var videoSourceConfiguration = profile
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "VideoSourceConfiguration");

        // videoSourceToken es el identificador requerido posteriormente por Imaging Service.
        var videoSourceToken = videoSourceConfiguration?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "SourceToken")?
            .Value;

        // videoEncoder contiene la configuración de codificación de video asociada al perfil.
        var videoEncoder = profile
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "VideoEncoderConfiguration");

        // resolution contiene Width y Height del perfil.
        var resolution = videoEncoder?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Resolution");

        // width y height representan la resolución real reportada por la cámara.
        var width = ParseInt(resolution, "Width");
        var height = ParseInt(resolution, "Height");

        // frameRate representa el límite de FPS del perfil.
        var frameRate = ParseInt(videoEncoder, "FrameRateLimit");

        // encoding contiene el codec, por ejemplo H264, H265 o JPEG.
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

    /// <summary>Convierte el contenido textual de un elemento XML en entero cuando es posible.</summary>
    private static int? ParseInt(XElement? parent, string elementName)
    {
        // value contiene el texto del elemento solicitado; si no existe queda null.
        var value = parent?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == elementName)?
            .Value;

        // result solo se utiliza cuando value representa un entero válido.
        return int.TryParse(value, out var result) ? result : null;
    }

    /// <summary>
    /// Construye la cabecera WS-Security cuando se proporcionan credenciales.
    /// </summary>
    private static string? BuildSecurity(string? username, string? password) =>
        (username, password) is (not null, not null)
            ? WsSecurityHeaderBuilder.Build(username!, password!)
            : null;
}
