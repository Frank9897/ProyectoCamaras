using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Implementación de IManufacturerResolver. Recibe TODOS los IManufacturerDetector
/// registrados por DI (sin conocerlos por nombre) y corre cada uno en paralelo con su
/// propio timeout interno. Se queda con el resultado de mayor Confidence y aplica sus
/// datos sobre el DiscoveredDevice. Agregar un detector nuevo (ej. mDNS, WS-Discovery
/// dirigido) no requiere tocar esta clase — solo registrarlo en el contenedor de DI.
/// </summary>
public sealed class ManufacturerResolver : IManufacturerResolver
{
    private readonly IEnumerable<IManufacturerDetector> _detectors;

    public ManufacturerResolver(IEnumerable<IManufacturerDetector> detectors)
    {
        _detectors = detectors;
    }

    public async Task ResolveAsync(DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        var tasks = _detectors.Select(async detector =>
        {
            try
            {
                return await detector.TryDetectAsync(device, cancellationToken);
            }
            catch
            {
                // Un detector individual que revienta no debe tumbar la resolución de todo
                // el dispositivo — se lo trata igual que "no aportó nada".
                return null;
            }
        });

        var results = (await Task.WhenAll(tasks))
            .Where(r => r is not null)
            .Cast<ManufacturerDetectionResult>()
            .ToList();

        if (results.Count == 0)
        {
            device.Status = DeviceStatus.Unknown;
            return;
        }

        var best = results.OrderByDescending(r => r.Confidence).First();

        if (!string.IsNullOrWhiteSpace(best.Manufacturer))
            device.Manufacturer = best.Manufacturer;
        if (!string.IsNullOrWhiteSpace(best.Model))
            device.Model = best.Model;
        if (!string.IsNullOrWhiteSpace(best.FirmwareVersion))
            device.FirmwareVersion = best.FirmwareVersion;
        if (!string.IsNullOrWhiteSpace(best.SerialNumber))
            device.SerialNumber = best.SerialNumber;

        // OR entre todos los resultados: si CUALQUIER detector confirmó ONVIF/HTTP,
        // vale, aunque no haya sido el de mayor confianza en cuanto a fabricante.
        device.OnvifSupported = results.Any(r => r.OnvifSupported);
        device.OnvifProfile = results.FirstOrDefault(r => r.OnvifSupported)?.OnvifProfile;
        device.RtspSupported = results.Any(r => r.RtspSupported);
        device.HttpSupported = results.Any(r => r.HttpSupported);
        device.HttpsSupported = results.Any(r => r.HttpsSupported);
        device.HttpPort ??= results.FirstOrDefault(r => r.HttpPort.HasValue)?.HttpPort;
        device.RtspPort ??= results.FirstOrDefault(r => r.RtspPort.HasValue)?.RtspPort;

        device.Status = device.OnvifSupported || device.HttpSupported
            ? DeviceStatus.Online
            : DeviceStatus.Unknown;
    }
}
