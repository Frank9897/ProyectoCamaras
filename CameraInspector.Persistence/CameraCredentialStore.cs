using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CameraInspector.Persistence;

/// <summary>
/// Persiste la relación entre una cámara y una referencia de credencial segura.
/// Esta clase nunca recibe ni almacena la contraseña real.
/// </summary>
public sealed class CameraCredentialStore : ICameraCredentialStore
{
    private readonly CameraInspectorDbContext _db;

    public CameraCredentialStore(CameraInspectorDbContext db)
    {
        // _db representa la unidad de trabajo SQLite utilizada para consultar y guardar la relación.
        _db = db;
    }

    public async Task<CameraCredentialInfo?> GetAsync(
        int cameraId,
        CancellationToken cancellationToken = default)
    {
        // entity contiene exclusivamente los metadatos almacenados en SQLite.
        var entity = await _db.CameraCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CameraId == cameraId, cancellationToken);

        if (entity is null)
            return null;

        return new CameraCredentialInfo
        {
            CameraId = entity.CameraId,
            Username = entity.Username,
            CredentialRef = entity.CredentialRef,
            LastVerifiedAt = entity.LastVerifiedAt
        };
    }

    public async Task SaveAsync(
        int cameraId,
        string username,
        Guid credentialRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        // entity representa la relación persistente que pertenece a la cámara indicada.
        var entity = await _db.CameraCredentials
            .FirstOrDefaultAsync(item => item.CameraId == cameraId, cancellationToken);

        if (entity is null)
        {
            // La cámara todavía no tiene credenciales vinculadas, por lo que creamos el registro.
            entity = new CameraCredentialEntity
            {
                CameraId = cameraId,
                Username = username,
                CredentialRef = credentialRef
            };

            _db.CameraCredentials.Add(entity);
        }
        else
        {
            // Actualizamos solamente los metadatos de la credencial; el secreto sigue fuera de SQLite.
            entity.Username = username;
            entity.CredentialRef = credentialRef;
            entity.LastVerifiedAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        int cameraId,
        CancellationToken cancellationToken = default)
    {
        // entity representa la relación que debe eliminarse del inventario.
        var entity = await _db.CameraCredentials
            .FirstOrDefaultAsync(item => item.CameraId == cameraId, cancellationToken);

        if (entity is null)
            return;

        _db.CameraCredentials.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
