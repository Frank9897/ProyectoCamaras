using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Ejecuta una comprobación ligera de comunicación y disponibilidad de vídeo.
/// </summary>
public interface ICameraHealthService
{
    Task<CameraHealthResult> CheckAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default);
}
