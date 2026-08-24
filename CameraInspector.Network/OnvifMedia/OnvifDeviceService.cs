using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Consulta el Device Service ONVIF y descubre las URLs reales de Media, Imaging,
/// PTZ y Events anunciadas por el firmware mediante GetCapabilities.
/// </summary>
public sealed class OnvifDeviceService : IOnvifDeviceService
{
    private const string GetCapabilitiesBody = """
        <tds:GetCapabilities xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
          <tds:Category>All</tds:Category>
        </tds:GetCapabilities>
        """;

    public async Task<OnvifServiceCapabilities?> GetCapabilitiesAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        var endpoint = $"http://{device.IpAddress}/onvif/device_service";
        var security = (username, password) is (not null, not null)
            ? WsSecurityHeaderBuilder.Build(username!, password!)
            : null;

        var document = await OnvifSoapClient.PostAsync(
            endpoint,
            GetCapabilitiesBody,
            security,
            cancellationToken);

        if (document is null)
            return null;

        return new OnvifServiceCapabilities
        {
            DeviceServiceXAddr = endpoint,
            MediaServiceXAddr = FindServiceXAddr(document, "Media"),
            ImagingServiceXAddr = FindServiceXAddr(document, "Imaging"),
            PtzServiceXAddr = FindServiceXAddr(document, "PTZ"),
            EventsServiceXAddr = FindServiceXAddr(document, "Events")
        };
    }

    private static string? FindServiceXAddr(
        System.Xml.Linq.XDocument document,
        string serviceElementName)
    {
        var service = document
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, serviceElementName, StringComparison.OrdinalIgnoreCase)
                && element.Elements().Any(child =>
                    string.Equals(child.Name.LocalName, "XAddr", StringComparison.OrdinalIgnoreCase)));

        return service?
            .Elements()
            .FirstOrDefault(child =>
                string.Equals(child.Name.LocalName, "XAddr", StringComparison.OrdinalIgnoreCase))?
            .Value
            .Trim();
    }
}
