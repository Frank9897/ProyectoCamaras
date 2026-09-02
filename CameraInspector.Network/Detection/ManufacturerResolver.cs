using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Combina todas las señales independientes de detección sin perder evidencia previa.
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

        foreach (var result in results)
        {
            device.AddEvidence(
                result.DetectorName,
                result.Confidence,
                result.EvidenceDetails,
                result.CameraEvidence);
        }

        if (results.Count == 0)
        {
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

        device.OnvifSupported |= results.Any(result => result.OnvifSupported);
        device.RtspSupported |= results.Any(result => result.RtspSupported);
        device.HttpSupported |= results.Any(result => result.HttpSupported);
        device.HttpsSupported |= results.Any(result => result.HttpsSupported);
        device.CameraEvidence |= results.Any(result => result.CameraEvidence);

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
        => device.CameraEvidence ||
           device.OnvifSupported ||
           device.RtspSupported ||
           device.DetectionEvidence.Any(item => item.IsCameraEvidence);
}
