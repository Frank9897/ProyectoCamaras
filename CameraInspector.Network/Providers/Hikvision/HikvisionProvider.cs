using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers.Hikvision;

/// <summary>
/// Provider propietario de Hikvision mediante ISAPI.
/// En esta primera versión solamente se implementan operaciones de lectura.
/// </summary>
public sealed class HikvisionProvider : ICameraProvider
{
    private readonly HttpClient _httpClient;

    public HikvisionProvider()
    {
        // _httpClient se utiliza únicamente para las llamadas ISAPI y mantiene un timeout corto.
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(4)
        };
    }

    public string Name => "Hikvision ISAPI";

    public bool CanHandle(DiscoveredDevice device)
    {
        // manufacturer y model son evidencias disponibles antes de autenticarnos.
        var manufacturer = device.Manufacturer ?? string.Empty;
        var model = device.Model ?? string.Empty;

        // Hikvision se acepta cuando OUI/banner/ONVIF ya aportaron suficiente evidencia del fabricante.
        return manufacturer.Contains("Hikvision", StringComparison.OrdinalIgnoreCase)
            || manufacturer.Contains("Hik Vision", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("DS-", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CameraProviderInfo?> GetDeviceInfoAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        // handler permite que HttpClient responda al desafío HTTP Digest del dispositivo.
        using var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(username, password),
            PreAuthenticate = false,
            AllowAutoRedirect = false
        };

        using var client = new HttpClient(handler)
        {
            Timeout = _httpClient.Timeout
        };

        // endpoint es la ruta ISAPI de lectura documentada por Hikvision para DeviceInfo.
        var endpoint = $"http://{device.IpAddress.Trim()}/ISAPI/System/deviceInfo";

        using var response = await client.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        // xml conserva la respuesta XML propietaria para extraer solo los campos comunes al inventario.
        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var document = XDocument.Parse(xml);

        return new CameraProviderInfo
        {
            ProviderName = Name,
            Manufacturer = ReadValue(document, "manufacturer") ?? ReadValue(document, "vendor"),
            Model = ReadValue(document, "model"),
            FirmwareVersion = ReadValue(document, "firmwareVersion"),
            SerialNumber = ReadValue(document, "serialNumber"),
            MacAddress = ReadValue(document, "macAddress"),
            DeviceType = ReadValue(document, "deviceType")
        };
    }

    private static string? ReadValue(XDocument document, string localName)
    {
        // value busca por nombre local para tolerar los distintos namespaces usados por firmwares Hikvision.
        var value = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?
            .Value;

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
