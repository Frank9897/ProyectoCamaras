using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CameraInspector.Persistence;

/// <summary>
/// Centro de alertas basado en CameraEvents. No necesita una tabla adicional:
/// los eventos históricos ya forman parte de la base local de Camera Inspector.
/// </summary>
public sealed class CameraAlertStore : ICameraAlertStore
{
    private readonly IDbContextFactory<CameraInspectorDbContext> _dbFactory;

    public CameraAlertStore(IDbContextFactory<CameraInspectorDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<AlertItem>> GetRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var rows = await db.CameraEvents
            .AsNoTracking()
            .Where(e => e.EventType.StartsWith("ALERT_") || e.EventType == "HISTORICAL_CHANGE")
            .Join(
                db.Cameras.AsNoTracking(),
                eventItem => eventItem.CameraId,
                camera => camera.Id,
                (eventItem, camera) => new { eventItem, camera })
            .OrderByDescending(row => row.eventItem.EventDate)
            .ThenByDescending(row => row.eventItem.Id)
            .Take(safeLimit)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new AlertItem
        {
            Id = row.eventItem.Id,
            CameraId = row.camera.Id,
            IpAddress = row.camera.Ip,
            Manufacturer = row.camera.Manufacturer,
            Model = row.camera.Model,
            Severity = GetSeverity(row.eventItem.EventType),
            Type = GetTypeLabel(row.eventItem.EventType),
            Description = row.eventItem.Description ?? "Sin descripción.",
            Date = row.eventItem.EventDate
        }).ToList();
    }

    private static string GetSeverity(string eventType) => eventType switch
    {
        "ALERT_IP_CONFLICT" => "CRÍTICA",
        "ALERT_DIAGNOSTIC" => "ALTA",
        "HISTORICAL_CHANGE" => "AVISO",
        _ => "INFO"
    };

    private static string GetTypeLabel(string eventType) => eventType switch
    {
        "ALERT_IP_CONFLICT" => "CONFLICTO IP",
        "ALERT_DIAGNOSTIC" => "DIAGNÓSTICO",
        "HISTORICAL_CHANGE" => "CAMBIO HISTÓRICO",
        _ => eventType.Replace('_', ' ')
    };
}
