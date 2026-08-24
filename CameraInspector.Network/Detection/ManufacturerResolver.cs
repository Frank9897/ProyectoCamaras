using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Implementación de IManufacturerResolver. Recibe TODOS los IManufacturerDetector
/// registrados por DI (sin conocerlos por nombre) y corre cada uno en paralelo con su
/// propio timeout interno. Se queda con el resultado de mayor Confidence y aplica sus
/// datos sobre el DiscoveredDevice.
/// </summary>
public sealed class ManufacturerResolver : IManufacturerResolver
{
    /// <summary>
    /// Conjunto de detectores registrados en DI. Cada detector aporta evidencia independiente.
    /// </summary>
    private readonly IEnumerable<IManufacturerDetector> _detectors;

    public ManufacturerResolver(IEnumerable<IManufacturerDetector> detectors)
    {
        // _detectors conserva la colección entregada por el contenedor de DI para ejecutar
        // todos los métodos de detección sin acoplar esta clase a implementaciones concretas.
        _detectors = detectors;
    }

    public async Task ResolveAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default)
    {
        // tasks contiene una tarea por detector. Ejecutarlos en paralelo reduce el tiempo total
        // de resolución porque OUI, HTTP y ONVIF pueden trabajar de manera independiente.
        var tasks = _detectors.Select(async detector =>
        {
            try
            {
                return await detector.TryDetectAsync(device, cancellationToken);
            }
            catch
            {
                // Un detector individual que falla no debe tumbar la resolución completa.
                // Se trata exactamente igual que si ese detector no hubiera encontrado nada.
                return null;
            }
        });

        // results contiene únicamente resultados válidos producidos por los detectores.
        var results = (await Task.WhenAll(tasks))
            .Where(result => result is not null)
            .Cast<ManufacturerDetectionResult>()
            .ToList();

        if (results.Count == 0)
        {
            // No hubo evidencia suficiente para identificar el dispositivo.
            // Dejamos el estado como Unknown y no modificamos los datos previamente descubiertos.
            device.Status = DeviceStatus.Unknown;
            return;
        }

        // best es el resultado con mayor confianza. Se utiliza para datos descriptivos como
        // fabricante, modelo, firmware y número de serie.
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

        // ONVIF se considera soportado si al menos un detector lo confirmó correctamente.
        device.OnvifSupported = results.Any(result => result.OnvifSupported);

        // OnvifProfile toma el primer perfil aportado por un detector ONVIF.
        device.OnvifProfile = results
            .FirstOrDefault(result => result.OnvifSupported)?.OnvifProfile;

        // OnvifDeviceServiceXAddr conserva la URL real reportada por el detector ONVIF.
        // No se reconstruye a partir de la IP porque el firmware puede publicar el servicio
        // en una ruta, puerto o dirección diferente.
        device.OnvifDeviceServiceXAddr = results
            .Where(result => result.OnvifSupported)
            .Select(result => result.OnvifDeviceServiceXAddr)
            .FirstOrDefault(address => !string.IsNullOrWhiteSpace(address));

        device.RtspSupported = results.Any(result => result.RtspSupported);
        device.HttpSupported = results.Any(result => result.HttpSupported);
        device.HttpsSupported = results.Any(result => result.HttpsSupported);

        // Solo rellenamos los puertos cuando todavía no existe un valor previamente detectado.
        // Esto evita que un detector de menor prioridad pise información más precisa.
        device.HttpPort ??= results
            .FirstOrDefault(result => result.HttpPort.HasValue)
            ?.HttpPort;

        device.RtspPort ??= results
            .FirstOrDefault(result => result.RtspPort.HasValue)
            ?.RtspPort;

        // Si algún detector confirmó ONVIF o HTTP, podemos considerar que el dispositivo
        // está accesible. La salud detallada se evaluará posteriormente en el diagnóstico.
        device.Status = device.OnvifSupported || device.HttpSupported
            ? DeviceStatus.Online
            : DeviceStatus.Unknown;
    }
}
