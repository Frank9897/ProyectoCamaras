using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato encargado de descubrir dispositivos ONVIF mediante WS-Discovery.
/// Permite indicar la interfaz local desde la que debe salir el multicast.
/// </summary>
public interface IOnvifDiscoveryService
{
    /// <summary>
    /// Envía un Probe WS-Discovery y devuelve los dispositivos ONVIF que respondan.
    /// </summary>
    /// <param name="networkInterface">
    /// Interfaz local que debe utilizarse para enviar y recibir el descubrimiento.
    /// Si es null, la implementación utilizará el comportamiento por defecto del sistema operativo.
    /// </param>
    /// <param name="cancellationToken">Token utilizado para cancelar la espera.</param>
    Task<IReadOnlyList<OnvifDiscoveryResult>> DiscoverAsync(
        NetworkInterfaceInfo? networkInterface = null,
        CancellationToken cancellationToken = default);
}
