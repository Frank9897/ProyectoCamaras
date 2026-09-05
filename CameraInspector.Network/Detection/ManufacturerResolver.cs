using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Combina todas las señales independientes de detección sin perder evidencia previa.
/// Las respuestas de autenticación no se convierten automáticamente en identidad de cámara.
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
            .Where(IsUsableIdentityResult)
            .OrderByDescending(result => result.Confidence)
            .FirstOrDefault();

        if (best is not null)
        {
            if (!string.IsNullOrWhiteSpace(best.Manufacturer))
                device.Manufacturer = best.Manufacturer;
            if (!string.IsNullOrWhiteSpace(best.Model))
                device.Model = best.Model;
            if (!string.IsNullOrWhiteSpace(best.FirmwareVersion))
                device.FirmwareVersion = best.FirmwareVersion;
            if (!string.IsNullOrWhiteSpace(best.SerialNumber))
                device.SerialNumber = best.SerialNumber;
        }

        // Una respuesta 401/403 del probe ONVIF solo demuestra que existe un endpoint
        // protegido; no confirma ONVIF. La confirmación requiere una respuesta SOAP válida
        // o WS-Discovery u otra evidencia ONVIF explícita.
        device.OnvifSupported |= results.Any(result =>
            result.OnvifSupported && !IsAuthenticationOnlyResult(result));

        device.RtspSupported |= results.Any(result => result.RtspSupported);
        device.HttpSupported |= results.Any(result => result.HttpSupported);
        device.HttpsSupported |= results.Any(result => result.HttpsSupported);

        // CameraEvidence solo se eleva con una detección que no sea una mera respuesta
        // de autenticación. Esto evita que routers/APs se conviertan en cámaras.
        device.CameraEvidence |= results.Any(result =>
            result.CameraEvidence && !IsAuthenticationOnlyResult(result));

        var onvifProfile = results
            .Where(result => result.OnvifSupported && !IsAuthenticationOnlyResult(result))
            .Select(result => result.OnvifProfile)
            .FirstOrDefault(profile => !string.IsNullOrWhiteSpace(profile));
        if (!string.IsNullOrWhiteSpace(onvifProfile))
            device.OnvifProfile = onvifProfile;

        var onvifXAddr = results
            .Where(result => result.OnvifSupported && !IsAuthenticationOnlyResult(result))
            .Select(result => result.OnvifDeviceServiceXAddr)
            .FirstOrDefault(address => !string.IsNullOrWhiteSpace(address));
        if (!string.IsNullOrWhiteSpace(onvifXAddr))
            device.OnvifDeviceServiceXAddr = onvifXAddr;

        if (!device.HttpPort.HasValue)
        {
            device.HttpPort = results
                .Where(IsUsableIdentityResult)
                .FirstOrDefault(result => result.HttpPort.HasValue)
                ?.HttpPort;
        }

        if (!device.RtspPort.HasValue)
        {
            device.RtspPort = results
                .Where(IsUsableIdentityResult)
                .FirstOrDefault(result => result.RtspPort.HasValue)
                ?.RtspPort;
        }

        device.Status = HasAnyCameraEvidence(device)
            ? DeviceStatus.Online
            : DeviceStatus.Unknown;
    }

    private static bool IsUsableIdentityResult(ManufacturerDetectionResult result)
        => !IsAuthenticationOnlyResult(result)
           && (!string.IsNullOrWhiteSpace(result.Manufacturer)
               || result.CameraEvidence && result.Confidence >= 0.99);

    private static bool IsAuthenticationOnlyResult(ManufacturerDetectionResult result)
    {
        var details = result.EvidenceDetails ?? string.Empty;
        return details.Contains("401", StringComparison.OrdinalIgnoreCase)
            || details.Contains("403", StringComparison.OrdinalIgnoreCase)
            || details.Contains("autentic", StringComparison.OrdinalIgnoreCase)
            || details.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || details.Contains("requiere acceso", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAnyCameraEvidence(DiscoveredDevice device)
        => device.CameraEvidence ||
           device.OnvifSupported ||
           device.HasOnvifMediaService ||
           device.HasOnvifImagingService ||
           device.HasOnvifPtzService ||
           device.DetectionEvidence.Any(item => item.IsCameraEvidence && !IsAuthenticationOnlyEvidence(item));

    private static bool IsAuthenticationOnlyEvidence(CameraDetectionEvidence evidence)
    {
        var details = evidence.Details ?? string.Empty;
        return details.Contains("401", StringComparison.OrdinalIgnoreCase)
            || details.Contains("403", StringComparison.OrdinalIgnoreCase)
            || details.Contains("autentic", StringComparison.OrdinalIgnoreCase)
            || details.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || details.Contains("requiere acceso", StringComparison.OrdinalIgnoreCase);
    }
}
