using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Orquesta todos los IManufacturerDetector registrados, corre cada uno con timeout propio,
/// y aplica sobre el DiscoveredDevice los datos del resultado con mayor Confidence.
/// Este es el único punto donde "gana" un detector sobre otro — los detectores en sí
/// no saben nada de los demás.
/// </summary>
public interface IManufacturerResolver
{
    /// <summary>Corre el pipeline completo y muta el dispositivo in-place con el mejor resultado encontrado.</summary>
    Task ResolveAsync(DiscoveredDevice device, CancellationToken cancellationToken = default);
}
