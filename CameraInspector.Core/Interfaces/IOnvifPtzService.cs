using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Expone operaciones PTZ genéricas de ONVIF.
/// La interfaz evita que la UI conozca SOAP, XML o detalles del fabricante.
/// </summary>
public interface IOnvifPtzService
{
    /// <summary>
    /// Ejecuta un movimiento continuo usando el perfil de video principal de la cámara.
    /// </summary>
    Task<bool> ContinuousMoveAsync(
        DiscoveredDevice device,
        OnvifPtzMoveRequest request,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detiene cualquier movimiento PTZ activo del perfil seleccionado.
    /// </summary>
    Task<bool> StopAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);
}
