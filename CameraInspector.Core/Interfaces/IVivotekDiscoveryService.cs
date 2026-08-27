using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato para el descubrimiento propietario de dispositivos VIVOTEK.
/// Se mantiene separado de ONVIF porque algunas cámaras VIVOTEK pueden anunciarse
/// mediante su broadcast propietario aunque todavía no respondan a WS-Discovery.
/// </summary>
public interface IVivotekDiscoveryService
{
    /// <summary>
    /// Envía una solicitud de descubrimiento por la interfaz indicada y devuelve
    /// los dispositivos VIVOTEK que contestaron.
    /// </summary>
    Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken = default);
}
