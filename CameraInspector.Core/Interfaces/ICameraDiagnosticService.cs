using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Ejecuta una batería de pruebas técnicas sobre un dispositivo descubierto.
/// La implementación pertenece a infraestructura; Core solo define el contrato.
/// </summary>
public interface ICameraDiagnosticService
{
    /// <summary>
    /// Ejecuta las pruebas disponibles y devuelve una instantánea de sus resultados.
    /// </summary>
    Task<IReadOnlyList<DiagnosticResult>> RunAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);
}
