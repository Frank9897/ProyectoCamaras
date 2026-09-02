using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detector basado en el OUI de la MAC. Es una señal débil: identifica fabricante,
/// pero por sí sola no confirma que el dispositivo sea una cámara.
/// </summary>
public sealed class OuiMacDetector : IManufacturerDetector
{
    public string Name => "OuiMac";

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
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(device.MacAddress) || device.MacAddress.Length < 8)
            return Task.FromResult<ManufacturerDetectionResult?>(null);

        var prefix = device.MacAddress[..8];
        if (!OuiTable.TryGetValue(prefix, out var manufacturer))
            return Task.FromResult<ManufacturerDetectionResult?>(null);

        return Task.FromResult<ManufacturerDetectionResult?>(new ManufacturerDetectionResult
        {
            DetectorName = Name,
            Confidence = 0.4,
            EvidenceDetails = $"OUI {prefix}: {manufacturer}",
            Manufacturer = manufacturer
        });
    }
}
