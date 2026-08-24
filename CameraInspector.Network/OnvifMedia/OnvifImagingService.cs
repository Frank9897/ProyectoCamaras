using System.Globalization;
using System.Security;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Implementación genérica del Imaging Service ONVIF.
/// Trabaja sobre el VideoSourceToken anunciado por Media Service.
/// </summary>
public sealed class OnvifImagingService : IOnvifImagingService
{
    private readonly IOnvifDeviceService _deviceService;
    private readonly IOnvifMediaService _mediaService;

    public OnvifImagingService(
        IOnvifDeviceService deviceService,
        IOnvifMediaService mediaService)
    {
        // _deviceService descubre el XAddr real de Imaging.
        _deviceService = deviceService;
        // _mediaService permite localizar el VideoSourceToken real del perfil de video.
        _mediaService = mediaService;
    }

    public async Task<OnvifImagingSettings?> GetImagingSettingsAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await ResolveImagingEndpointAsync(device, username, password, cancellationToken);
        var sourceToken = await ResolveVideoSourceTokenAsync(device, username, password, cancellationToken);

        if (endpoint is null || sourceToken is null)
            return null;

        var token = SecurityElement.Escape(sourceToken);
        var body = $"""
                   <timg:GetImagingSettings xmlns:timg="http://www.onvif.org/ver20/imaging/wsdl">
                     <timg:VideoSourceToken>{token}</timg:VideoSourceToken>
                   </timg:GetImagingSettings>
                   """;

        var document = await OnvifSoapClient.PostAsync(
            endpoint,
            body,
            BuildSecurity(username, password),
            cancellationToken);

        if (document is null)
            return null;

        // imaging contiene el bloque ImagingSettings del dispositivo.
        var imaging = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "ImagingSettings20");

        if (imaging is null)
            return null;

        return new OnvifImagingSettings
        {
            Brightness = ParseFloat(imaging, "Brightness"),
            ColorSaturation = ParseFloat(imaging, "ColorSaturation"),
            Contrast = ParseFloat(imaging, "Contrast"),
            Sharpness = ParseFloat(imaging, "Sharpness"),
            IrCutFilter = imaging
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "IrCutFilter")?
                .Value
        };
    }

    public async Task<bool> SetImagingSettingsAsync(
        DiscoveredDevice device,
        OnvifImagingSettings settings,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var endpoint = await ResolveImagingEndpointAsync(device, username, password, cancellationToken);
        var sourceToken = await ResolveVideoSourceTokenAsync(device, username, password, cancellationToken);

        if (endpoint is null || sourceToken is null)
            return false;

        var token = SecurityElement.Escape(sourceToken);
        var body = $"""
                   <timg:SetImagingSettings xmlns:timg="http://www.onvif.org/ver20/imaging/wsdl"
                                            xmlns:tt="http://www.onvif.org/ver10/schema">
                     <timg:VideoSourceToken>{token}</timg:VideoSourceToken>
                     <tt:ImagingSettings>
                       {(settings.Brightness is float brightness ? $"<tt:Brightness>{brightness.ToString(CultureInfo.InvariantCulture)}</tt:Brightness>" : string.Empty)}
                       {(settings.ColorSaturation is float saturation ? $"<tt:ColorSaturation>{saturation.ToString(CultureInfo.InvariantCulture)}</tt:ColorSaturation>" : string.Empty)}
                       {(settings.Contrast is float contrast ? $"<tt:Contrast>{contrast.ToString(CultureInfo.InvariantCulture)}</tt:Contrast>" : string.Empty)}
                       {(settings.Sharpness is float sharpness ? $"<tt:Sharpness>{sharpness.ToString(CultureInfo.InvariantCulture)}</tt:Sharpness>" : string.Empty)}
                       {(string.IsNullOrWhiteSpace(settings.IrCutFilter) ? string.Empty : $"<tt:IrCutFilter>{SecurityElement.Escape(settings.IrCutFilter)}</tt:IrCutFilter>")}
                     </tt:ImagingSettings>
                     <timg:ForcePersistence>true</timg:ForcePersistence>
                   </timg:SetImagingSettings>
                   """;

        var document = await OnvifSoapClient.PostAsync(
            endpoint,
            body,
            BuildSecurity(username, password),
            cancellationToken);

        return document is not null;
    }

    private async Task<string?> ResolveImagingEndpointAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(device.OnvifImagingServiceXAddr))
            return device.OnvifImagingServiceXAddr;

        var capabilities = await _deviceService.GetCapabilitiesAsync(
            device,
            username,
            password,
            cancellationToken);

        return capabilities?.ImagingServiceXAddr;
    }

    private async Task<string?> ResolveVideoSourceTokenAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var mediaXAddr = device.OnvifMediaServiceXAddr;
        if (string.IsNullOrWhiteSpace(mediaXAddr))
        {
            var capabilities = await _deviceService.GetCapabilitiesAsync(
                device,
                username,
                password,
                cancellationToken);
            mediaXAddr = capabilities?.MediaServiceXAddr;
        }

        if (string.IsNullOrWhiteSpace(mediaXAddr))
            return null;

        var profiles = await _mediaService.GetProfilesAsync(
            device,
            mediaXAddr,
            username,
            password,
            cancellationToken);

        return profiles
            .Select(profile => profile.VideoSourceToken)
            .FirstOrDefault(token => !string.IsNullOrWhiteSpace(token));
    }

    private static float? ParseFloat(XElement parent, string elementName)
    {
        // value contiene el texto del ajuste solicitado; puede faltar en cámaras con capacidades parciales.
        var value = parent
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == elementName)?
            .Value;

        // result convierte la representación decimal usando cultura invariante para evitar depender del idioma de Windows.
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static string? BuildSecurity(string? username, string? password) =>
        username is not null && password is not null
            ? WsSecurityHeaderBuilder.Build(username, password)
            : null;
}
