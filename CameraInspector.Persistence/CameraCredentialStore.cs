using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CameraInspector.Persistence;

/// <summary>
/// Persiste la relación entre una cámara y una referencia de credencial segura.
/// Utiliza IDbContextFactory para que cada operación tenga una unidad de trabajo independiente.
/// </summary>
public sealed class CameraCredentialStore : ICameraCredentialStore
{
    private readonly IDbContextFactory<CameraInspectorDbContext> _dbFactory;

    public CameraCredentialStore(IDbContextFactory<CameraInspectorDbContext> dbFactory)
    {
        // _dbFactory crea un DbContext exclusivo para cada operación de credenciales.
        _dbFactory = dbFactory;
    }

    public async Task<CameraCredentialInfo?> GetAsync(
        int cameraId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // entity contiene exclusivamente metadatos; el secreto real sigue en Credential Manager.
        var entity = await db.CameraCredentials
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

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // entity representa la relación persistente que pertenece a la cámara indicada.
        var entity = await db.CameraCredentials
            .FirstOrDefaultAsync(item => item.CameraId == cameraId, cancellationToken);

        if (entity is null)
        {
            // La cámara todavía no tiene una credencial vinculada.
            entity = new CameraCredentialEntity
            {
                CameraId = cameraId,
                Username = username,
                CredentialRef = credentialRef
            };

            db.CameraCredentials.Add(entity);
        }
        else
        {
            // Solo actualizamos la referencia segura y el usuario; nunca el secreto.
            entity.Username = username;
            entity.CredentialRef = credentialRef;
            entity.LastVerifiedAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkVerifiedAsync(
        int cameraId,
        DateTimeOffset verifiedAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // entity representa la relación cuya última verificación vamos a actualizar.
        var entity = await db.CameraCredentials
            .FirstOrDefaultAsync(item => item.CameraId == cameraId, cancellationToken);

        if (entity is null)
            return;

        // LastVerifiedAt cambia después de una operación que confirmó el uso de la credencial.
        entity.LastVerifiedAt = verifiedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        int cameraId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // entity representa la relación que debe desaparecer del inventario.
        var entity = await db.CameraCredentials
            .FirstOrDefaultAsync(item => item.CameraId == cameraId, cancellationToken);

        if (entity is null)
            return;

        db.CameraCredentials.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }
}
