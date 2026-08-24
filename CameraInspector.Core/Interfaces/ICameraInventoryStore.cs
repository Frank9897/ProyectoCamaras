using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato para crear o actualizar una cámara del inventario persistente.
/// La implementación decide cómo resolver identidad estable y almacenar cambios.
/// </summary>
public interface ICameraInventoryStore
{
    /// <summary>
    /// Inserta una cámara nueva o actualiza una existente y devuelve su Id SQLite.
    /// </summary>
    Task<int> UpsertAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default);
}
