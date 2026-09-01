using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Combina todas las señales independientes de detección sin perder evidencia previa.
/// Los detectores pueden confirmar nuevas capacidades, pero nunca deben borrar una capacidad
/// descubierta previamente por Ping/ARP, TCP, SSDP, VIVOTEK u otra capa.
/// </summary>
public sealed class ManufacturerResolver : IManufacturerResolver
{
    private readonly IEnumerable<IManufacturerDetector> _detectors;

    public ManufacturerResolver(IEnumerable<IManufacturerDetector> detectors)
    {
        _detectors = detectors;
    }

    public async Task ResolveAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default)
    {
        var tasks = _detectors.Select(async detector =>
        {
            try
            {
                return await detector.TryDetectAsync(device, cancellationToken);
            }
            catch
            {
                return null;
            }
        });

        var results = (await Task.WhenAll(tasks))
            .Where(result => result is not null)
            .Cast<ManufacturerDetectionResult>()
            .ToList();

        if (results.Count == 0)
        {
            // No borrar evidencia de red que ya exista en el dispositivo.
            device.Status = HasAnyCameraEvidence(device)
                ? DeviceStatus.Online
                : DeviceStatus.Unknown;
            return;
        }

        var best = results
            .OrderByDescending(result => result.Confidence)
            .First();

        if (!string.IsNullOrWhiteSpace(best.Manufacturer))
            device.Manufacturer = best.Manufacturer;
        if (!string.IsNullOrWhiteSpace(best.Model))
            device.Model = best.Model;
        if (!string.IsNullOrWhiteSpace(best.FirmwareVersion))
            device.FirmwareVersion = best.FirmwareVersion;
        if (!string.IsNullOrWhiteSpace(best.SerialNumber))
            device.SerialNumber = best.SerialNumber;

        // Las evidencias son acumulativas: un detector que no vea ONVIF no invalida una detección ONVIF previa.
        device.OnvifSupported |= results.Any(result => result.OnvifSupported);
        device.RtspSupported |= results.Any(result => result.RtspSupported);
        device.HttpSupported |= results.Any(result => result.HttpSupported);
        device.HttpsSupported |= results.Any(result => result.HttpsSupported);

        var onvifProfile = results
            .FirstOrDefault(result => result.OnvifSupported)?.OnvifProfile;
        if (!string.IsNullOrWhiteSpace(onvifProfile))
            device.OnvifProfile = onvifProfile;

        var onvifXAddr = results
            .Where(result => result.OnvifSupported)
            .Select(result => result.OnvifDeviceServiceXAddr)
            .FirstOrDefault(address => !string.IsNullOrWhiteSpace(address));
        if (!string.IsNullOrWhiteSpace(onvifXAddr))
            device.OnvifDeviceServiceXAddr = onvifXAddr;

        if (!device.HttpPort.HasValue)
        {
            device.HttpPort = results
                .FirstOrDefault(result => result.HttpPort.HasValue)
                ?.HttpPort;
        }

        if (!device.RtspPort.HasValue)
        {
            device.RtspPort = results
                .FirstOrDefault(result => result.RtspPort.HasValue)
                ?.RtspPort;
        }

        device.Status = HasAnyCameraEvidence(device)
            ? DeviceStatus.Online
            : DeviceStatus.Unknown;
    }

    private static bool HasAnyCameraEvidence(DiscoveredDevice device)
        => device.OnvifSupported ||
           device.RtspSupported ||
           device.HttpSupported ||
           device.HttpsSupported ||
           string.Equals(device.Manufacturer, "VIVOTEK", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(device.Manufacturer, "Hikvision", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(device.Manufacturer, "Dahua", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(device.Manufacturer, "Axis", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(device.Manufacturer, "Uniview", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(device.Manufacturer, "Reolink", StringComparison.OrdinalIgnoreCase);
}
