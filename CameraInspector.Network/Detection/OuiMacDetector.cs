using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detector basado en el OUI (primeros 3 octetos de la MAC), asignados por IEEE a cada
/// fabricante. Es una señal DÉBIL a propósito (Confidence baja): un OUI de Hikvision puede
/// corresponder a una cámara, un NVR, o incluso otro producto de la misma marca — por eso
/// nunca debería ganarle a una respuesta ONVIF real si ambas están disponibles.
/// </summary>
public sealed class OuiMacDetector : IManufacturerDetector
{
    public string Name => "OuiMac";

    // Tabla mínima de arranque — ampliable sin tocar el resolver ni otros detectores.
    // Fuente: prefijos OUI públicos de IEEE para fabricantes de CCTV conocidos.
    private static readonly Dictionary<string, string> OuiTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["4C:11:BF"] = "Hikvision",
        ["44:19:B6"] = "Hikvision",
        ["8C:E7:48"] = "Hikvision",
        ["BC:AD:28"] = "Dahua",
        ["3C:EF:8C"] = "Dahua",
        ["90:02:A9"] = "Dahua",
        ["00:40:8C"] = "Axis",
        ["AC:CC:8E"] = "Axis",
        ["EC:71:DB"] = "Uniview",
        ["24:0F:9D"] = "Uniview",
        ["EC:44:76"] = "Reolink",
    };

    public Task<ManufacturerDetectionResult?> TryDetectAsync(
        DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(device.MacAddress) || device.MacAddress.Length < 8)
            return Task.FromResult<ManufacturerDetectionResult?>(null);

        var prefix = device.MacAddress[..8]; // "AA:BB:CC"

        if (!OuiTable.TryGetValue(prefix, out var manufacturer))
            return Task.FromResult<ManufacturerDetectionResult?>(null);

        return Task.FromResult<ManufacturerDetectionResult?>(new ManufacturerDetectionResult
        {
            DetectorName = Name,
            Confidence = 0.4,
            Manufacturer = manufacturer
        });
    }
}
