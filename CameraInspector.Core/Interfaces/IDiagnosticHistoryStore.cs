using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato de persistencia para guardar los resultados de una ejecución de diagnóstico.
/// Core define la operación; SQLite implementa el almacenamiento en Persistence.
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
}
