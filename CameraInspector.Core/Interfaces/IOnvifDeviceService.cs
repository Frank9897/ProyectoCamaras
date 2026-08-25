using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Acceso al Device Service ONVIF.
/// Su responsabilidad es obtener identidad del dispositivo, descubrir servicios
/// y consultar la configuración de red expuesta por ONVIF.
/// </summary>
public interface IOnvifDeviceService
{
    /// <summary>Obtiene fabricante, modelo, firmware, número de serie y hardware id mediante ONVIF.</summary>
    Task<OnvifDeviceInformation?> GetDeviceInformationAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>Obtiene las URLs reales de Device, Media, Imaging, PTZ y Events.</summary>
    Task<OnvifServiceCapabilities?> GetCapabilitiesAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta las interfaces, protocolos y gateways de red de la cámara.
    /// Es una operación de solo lectura; no modifica la configuración del dispositivo.
    /// </summary>
    Task<OnvifNetworkConfiguration?> GetNetworkConfigurationAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default);
}
