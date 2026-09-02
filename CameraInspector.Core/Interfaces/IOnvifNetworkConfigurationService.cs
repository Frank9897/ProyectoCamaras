using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Operaciones de administración de red y sistema ONVIF.
/// Las operaciones de escritura requieren una acción explícita del técnico.
/// </summary>
public interface IOnvifNetworkConfigurationService
{
    Task<OnvifNetworkChangeResult> SetIPv4Async(
        DiscoveredDevice device,
        string username,
        string password,
        string interfaceToken,
        bool useDhcp,
        string? ipv4Address,
        int? prefixLength,
        CancellationToken cancellationToken = default);

    Task<OnvifNetworkChangeResult> SetDefaultGatewayAsync(
        DiscoveredDevice device,
        string username,
        string password,
        string? gatewayAddress,
        CancellationToken cancellationToken = default);

    Task<OnvifNetworkChangeResult> SetHostnameAsync(
        DiscoveredDevice device,
        string username,
        string password,
        string hostname,
        CancellationToken cancellationToken = default);

    Task<OnvifNetworkChangeResult> RebootAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task<OnvifNetworkChangeResult> FactoryResetAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default);
}
