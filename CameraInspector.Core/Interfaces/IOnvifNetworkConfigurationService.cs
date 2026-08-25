using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Operaciones de administración de red ONVIF.
/// Las operaciones de escritura requieren una acción explícita del técnico.
/// </summary>
public interface IOnvifNetworkConfigurationService
{
    /// <summary>
    /// Cambia DHCP o la configuración IPv4 estática de una interfaz ONVIF.
    /// </summary>
    Task<OnvifNetworkChangeResult> SetIPv4Async(
        DiscoveredDevice device,
        string username,
        string password,
        string interfaceToken,
        bool useDhcp,
        string? ipv4Address,
        int? prefixLength,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambia el gateway IPv4 por defecto.
    /// </summary>
    Task<OnvifNetworkChangeResult> SetDefaultGatewayAsync(
        DiscoveredDevice device,
        string username,
        string password,
        string? gatewayAddress,
        CancellationToken cancellationToken = default);
}
