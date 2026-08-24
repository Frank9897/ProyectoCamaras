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
        // _db representa la unidad de trabajo de EF Core utilizada para guardar y consultar el historial.
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

        // testDate representa un instante común para toda la ejecución del diagnóstico.
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

        await _db.CameraTests.AddRangeAsync(entities, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        // camera representa el inventario persistente al que pertenece esta ejecución.
        var camera = await _db.Cameras
            .FirstOrDefaultAsync(c => c.Id == cameraId, cancellationToken);

        if (camera is null)
            return;

        // LastTest permite mostrar rápidamente cuándo fue la última batería ejecutada.
        camera.LastTest = testDate;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DiagnosticHistoryItem>> GetRecentAsync(
        int cameraId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (cameraId <= 0)
            return [];

        // safeLimit evita solicitar una cantidad descontrolada de registros desde la UI.
        var safeLimit = Math.Clamp(limit, 1, 500);

        // rows contiene solo las columnas necesarias para presentar el historial.
        var rows = await _db.CameraTests
            .AsNoTracking()
            .Where(test => test.CameraId == cameraId)
            .OrderByDescending(test => test.TestDate)
            .ThenByDescending(test => test.Id)
            .Take(safeLimit)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new DiagnosticHistoryItem
        {
            Id = row.Id,
            TestName = row.TestName,
            Result = row.Result,
            ResponseTimeMs = row.ResponseTimeMs,
            Message = row.ErrorMessage,
            TestDate = row.TestDate
        }).ToList();
    }
}
