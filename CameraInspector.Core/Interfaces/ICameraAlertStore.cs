using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Consulta las alertas persistidas generadas por cambios históricos, conflictos y diagnósticos.
/// </summary>
public interface ICameraAlertStore
{
    Task<IReadOnlyList<AlertItem>> GetRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);
}
