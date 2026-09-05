using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CameraInspector.Persistence;

/// <summary>
/// Persiste dispositivos identificados como cámaras.
/// Utiliza IDbContextFactory para que el store pueda vivir durante toda la aplicación
/// sin conservar un DbContext compartido entre operaciones concurrentes.
/// </summary>
public sealed class CameraInventoryStore : ICameraInventoryStore
{
    private readonly IDbContextFactory<CameraInspectorDbContext> _dbFactory;

    public CameraInventoryStore(IDbContextFactory<CameraInspectorDbContext> dbFactory)
    {
        // _dbFactory crea un DbContext independiente para cada operación de inventario.
        _dbFactory = dbFactory;
    }

    public async Task<int> UpsertAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        // db representa una unidad de trabajo SQLite exclusiva para este Upsert.
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // camera busca primero por MAC porque puede sobrevivir a cambios de IP.
        CameraEntity? camera = null;
        var matchedByMac = false;

        if (!string.IsNullOrWhiteSpace(device.MacAddress))
        {
            camera = await db.Cameras
                .FirstOrDefaultAsync(c => c.Mac == device.MacAddress, cancellationToken);
            matchedByMac = camera is not null;
        }

        // Si no existe MAC, utilizamos la IP como mecanismo secundario de correlación.
        camera ??= await db.Cameras
            .FirstOrDefaultAsync(c => c.Ip == device.IpAddress, cancellationToken);

        if (camera is null)
        {
            // Una cámara nueva obtiene un registro persistente con la identidad disponible.
            camera = new CameraEntity
            {
                Ip = device.IpAddress,
                Mac = device.MacAddress,
                Manufacturer = device.Manufacturer,
                Model = device.Model,
                Firmware = device.FirmwareVersion,
                SerialNumber = device.SerialNumber,
                Hostname = device.Hostname,
                FirstSeen = device.FirstSeenAt,
                LastSeen = device.LastSeenAt
            };

            db.Cameras.Add(camera);
        }
        else
        {
            // Si la IP ya pertenece a otra MAC, no sobrescribimos la identidad histórica.
            // Esto evita convertir un conflicto de IP en un falso cambio de cámara.
            var ipConflict = !matchedByMac
                && !string.IsNullOrWhiteSpace(camera.Mac)
                && !string.IsNullOrWhiteSpace(device.MacAddress)
                && !camera.Mac.Equals(device.MacAddress, StringComparison.OrdinalIgnoreCase);

            if (ipConflict)
            {
                await SaveIpConflictAsync(db, camera, device, cancellationToken);
            }
            else
            {
                // Los cambios de identidad/configuración se registran antes de actualizar el inventario.
                await RecordChangesAsync(db, camera, device, cancellationToken);

                camera.Ip = device.IpAddress;
                camera.Mac ??= device.MacAddress;
                camera.Manufacturer = device.Manufacturer ?? camera.Manufacturer;
                camera.Model = device.Model ?? camera.Model;
                camera.Firmware = device.FirmwareVersion ?? camera.Firmware;
                camera.SerialNumber = device.SerialNumber ?? camera.SerialNumber;
                camera.Hostname = device.Hostname ?? camera.Hostname;
                camera.LastSeen = device.LastSeenAt;
            }
        }

        // Info conserva capacidades extensibles sin obligarnos a crear una migración por cada propiedad nueva.
        camera.Info = System.Text.Json.JsonSerializer.Serialize(new
        {
            device.OnvifSupported,
            device.OnvifProfile,
            device.OnvifDeviceServiceXAddr,
            device.OnvifMediaServiceXAddr,
            device.OnvifImagingServiceXAddr,
            device.OnvifPtzServiceXAddr,
            device.OnvifEventsServiceXAddr,
            device.RtspSupported,
            device.HttpSupported,
            device.HttpsSupported,
            device.HttpPort,
            device.RtspPort
        });

        await db.SaveChangesAsync(cancellationToken);
        return camera.Id;
    }

    private static async Task RecordChangesAsync(
        CameraInspectorDbContext db,
        CameraEntity camera,
        DiscoveredDevice device,
        CancellationToken cancellationToken)
    {
        var changes = new List<(string Field, string? Previous, string? Current)>
        {
            ("IP", camera.Ip, device.IpAddress),
            ("MAC", camera.Mac, device.MacAddress),
            ("FABRICANTE", camera.Manufacturer, device.Manufacturer),
            ("MODELO", camera.Model, device.Model),
            ("FIRMWARE", camera.Firmware, device.FirmwareVersion),
            ("SERIAL", camera.SerialNumber, device.SerialNumber),
            ("HOSTNAME", camera.Hostname, device.Hostname)
        };

        foreach (var change in changes)
        {
            if (string.IsNullOrWhiteSpace(change.Current))
                continue;

            if (string.Equals(change.Previous, change.Current, StringComparison.OrdinalIgnoreCase))
                continue;

            db.CameraEvents.Add(new CameraEventEntity
            {
                CameraId = camera.Id,
                EventType = "HISTORICAL_CHANGE",
                Description = $"{change.Field}: {change.Previous ?? "sin dato"} → {change.Current}",
                EventDate = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SaveIpConflictAsync(
        CameraInspectorDbContext db,
        CameraEntity camera,
        DiscoveredDevice device,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var duplicateAlert = await db.CameraEvents.AnyAsync(
            e => e.CameraId == camera.Id
                && e.EventType == "ALERT_IP_CONFLICT"
                && e.Description == $"IP {device.IpAddress}: MAC histórica {camera.Mac} · MAC observada {device.MacAddress}",
            cancellationToken);

        if (!duplicateAlert)
        {
            db.CameraEvents.Add(new CameraEventEntity
            {
                CameraId = camera.Id,
                EventType = "ALERT_IP_CONFLICT",
                Description = $"IP {device.IpAddress}: MAC histórica {camera.Mac} · MAC observada {device.MacAddress}",
                EventDate = now
            });
        }

        // LastSeen sigue representando que la identidad histórica respondió en la red.
        camera.LastSeen = device.LastSeenAt;
        await db.SaveChangesAsync(cancellationToken);
    }
}
