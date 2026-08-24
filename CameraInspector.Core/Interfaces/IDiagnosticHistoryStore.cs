using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato de persistencia para guardar y consultar resultados de diagnóstico.
/// Core define las operaciones; SQLite implementa el almacenamiento en Persistence.
/// </summary>
public interface IDiagnosticHistoryStore
{
    /// <summary>
    /// Guarda los resultados de diagnóstico asociados a una cámara persistida.
    /// </summary>
    Task SaveAsync(
        int cameraId,
        IReadOnlyList<DiagnosticResult> results,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve los últimos resultados de una cámara, ordenados desde el más reciente.
    /// </summary>
    Task<IReadOnlyList<DiagnosticHistoryItem>> GetRecentAsync(
        int cameraId,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
