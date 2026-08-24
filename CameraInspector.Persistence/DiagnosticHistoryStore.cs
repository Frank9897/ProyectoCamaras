using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CameraInspector.Persistence;

/// <summary>
/// Implementación de IDiagnosticHistoryStore sobre SQLite/EF Core.
/// Cada resultado se transforma en una fila de CameraTestEntity para conservar el historial.
/// </summary>
public sealed class DiagnosticHistoryStore : IDiagnosticHistoryStore
{
    private readonly CameraInspectorDbContext _db;

    public DiagnosticHistoryStore(CameraInspectorDbContext db)
    {
        // _db representa la unidad de trabajo de EF Core utilizada para guardar el historial.
        _db = db;
    }

    public async Task SaveAsync(
        int cameraId,
        IReadOnlyList<DiagnosticResult> results,
        CancellationToken cancellationToken = default)
    {
        if (cameraId <= 0)
            throw new ArgumentOutOfRangeException(nameof(cameraId));

        if (results.Count == 0)
            return;

        // testDate representa un instante común para la ejecución completa del diagnóstico.
        // Se usa UTC para evitar inconsistencias entre equipos y reportes posteriores.
        var testDate = DateTimeOffset.UtcNow;

        // entities transforma los resultados de dominio en entidades persistentes.
        var entities = results.Select(result => new CameraTestEntity
        {
            CameraId = cameraId,
            TestType = "Diagnostic",
            TestName = result.TestName,
            Result = result.NotSupported
                ? "SKIPPED"
                : result.Success ? "OK" : "ERROR",
            ResponseTimeMs = result.Duration.TotalMilliseconds > int.MaxValue
                ? int.MaxValue
                : Math.Max(0, (int)result.Duration.TotalMilliseconds),
            ErrorMessage = result.Success || result.NotSupported
                ? null
                : result.Message,
            TestDate = testDate
        }).ToList();

        // AddRange agrega todas las pruebas a la unidad de trabajo sin ejecutar un INSERT por cada elemento.
        await _db.CameraTests.AddRangeAsync(entities, cancellationToken);

        // Guardamos una sola transacción lógica para que el historial de la ejecución quede completo.
        await _db.SaveChangesAsync(cancellationToken);

        // Actualizamos LastTest para que el inventario sepa cuándo se ejecutó por última vez un diagnóstico.
        var camera = await _db.Cameras
            .FirstOrDefaultAsync(c => c.Id == cameraId, cancellationToken);

        if (camera is null)
            return;

        // LastTest cambia únicamente cuando la cámara existe realmente en el inventario persistido.
        camera.LastTest = testDate;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
