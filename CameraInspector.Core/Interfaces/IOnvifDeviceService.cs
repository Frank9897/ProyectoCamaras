using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Acceso al Device Service ONVIF.
/// Su responsabilidad es obtener identidad del dispositivo y descubrir las URLs reales
/// de los servicios que el firmware anuncia mediante GetCapabilities.
/// </summary>
public interface IOnvifDeviceService
{
    /// <summary>
    /// Obtiene fabricante, modelo, firmware, número de serie y hardware id mediante ONVIF.
    /// </summary>
    Task<OnvifDeviceInformation?> GetDeviceInformationAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene las URLs reales de Device, Media, Imaging, PTZ y Events.
    /// </summary>
    Task<OnvifServiceCapabilities?> GetCapabilitiesAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);
}
