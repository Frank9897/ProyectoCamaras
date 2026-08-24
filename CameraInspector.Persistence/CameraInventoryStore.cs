using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CameraInspector.Persistence;

/// <summary>
/// Persiste dispositivos identificados como cámaras.
/// La MAC es la clave de correlación preferida; la IP funciona como respaldo cuando no existe MAC.
/// </summary>
public sealed class CameraInventoryStore : ICameraInventoryStore
{
    private readonly CameraInspectorDbContext _db;

    public CameraInventoryStore(CameraInspectorDbContext db)
    {
        // _db representa la unidad de trabajo SQLite utilizada por el inventario.
        _db = db;
    }

    public async Task<int> UpsertAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        // camera busca primero por MAC porque puede sobrevivir a cambios de IP.
        CameraEntity? camera = null;

        if (!string.IsNullOrWhiteSpace(device.MacAddress))
        {
            camera = await _db.Cameras
                .FirstOrDefaultAsync(c => c.Mac == device.MacAddress, cancellationToken);
        }

        // Si no existe MAC, utilizamos la IP como mecanismo secundario de correlación.
        camera ??= await _db.Cameras
            .FirstOrDefaultAsync(c => c.Ip == device.IpAddress, cancellationToken);

        // firstSeen representa el momento en que la cámara entró por primera vez al inventario.
        var firstSeen = device.FirstSeenAt;

        if (camera is null)
        {
            // Una cámara nueva obtiene un registro persistente con la identidad disponible en este momento.
            camera = new CameraEntity
            {
                Ip = device.IpAddress,
                Mac = device.MacAddress,
                Manufacturer = device.Manufacturer,
                Model = device.Model,
                Firmware = device.FirmwareVersion,
                SerialNumber = device.SerialNumber,
                Hostname = device.Hostname,
                FirstSeen = firstSeen,
                LastSeen = device.LastSeenAt
            };

            // Add incorpora la entidad al contexto; SaveChanges asignará el Id SQLite.
            _db.Cameras.Add(camera);
        }
        else
        {
            // Los campos se actualizan solo cuando el descubrimiento proporciona un valor válido.
            camera.Ip = device.IpAddress;
            camera.Mac ??= device.MacAddress;
            camera.Manufacturer = device.Manufacturer ?? camera.Manufacturer;
            camera.Model = device.Model ?? camera.Model;
            camera.Firmware = device.FirmwareVersion ?? camera.Firmware;
            camera.SerialNumber = device.SerialNumber ?? camera.SerialNumber;
            camera.Hostname = device.Hostname ?? camera.Hostname;
            camera.LastSeen = device.LastSeenAt;
        }

        // Info almacena capacidades no tabuladas en columnas independientes.
        // Se mantiene legible y permite ampliar el inventario sin migrar por cada nueva capacidad.
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

        // SaveChanges persiste tanto la alta como la actualización y deja disponible el Id real de la cámara.
        await _db.SaveChangesAsync(cancellationToken);

        return camera.Id;
    }
}
