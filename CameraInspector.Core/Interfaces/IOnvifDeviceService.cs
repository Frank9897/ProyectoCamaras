using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Acceso al Device Service ONVIF. Su responsabilidad es descubrir capacidades
/// y URLs de los servicios que anuncia el dispositivo.
/// </summary>
public interface IOnvifDeviceService
{
    Task<OnvifServiceCapabilities?> GetCapabilitiesAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);
}
