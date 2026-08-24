using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato encargado de descubrir dispositivos ONVIF mediante WS-Discovery.
/// La interfaz vive en Core para que la capa de aplicación no dependa de UDP ni de una implementación concreta.
/// </summary>
public interface IOnvifDiscoveryService
{
    /// <summary>
    /// Envía un Probe WS-Discovery y devuelve los dispositivos ONVIF que respondan dentro del tiempo indicado.
    /// </summary>
    /// <param name="cancellationToken">Token utilizado para cancelar la espera y cerrar el descubrimiento.</param>
    Task<IReadOnlyList<OnvifDiscoveryResult>> DiscoverAsync(
        CancellationToken cancellationToken = default);
}