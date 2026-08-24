using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Persiste únicamente la relación entre una cámara del inventario y una credencial segura.
/// El secreto real se mantiene fuera de SQLite y se recupera mediante ICredentialStore.
/// </summary>
public interface ICameraCredentialStore
{
    /// <summary>Obtiene la credencial asociada a una cámara, si existe.</summary>
    Task<CameraCredentialInfo?> GetAsync(
        int cameraId,
        CancellationToken cancellationToken = default);

    /// <summary>Crea o actualiza la referencia de credencial asociada a la cámara.</summary>
    Task SaveAsync(
        int cameraId,
        string username,
        Guid credentialRef,
        CancellationToken cancellationToken = default);

    /// <summary>Actualiza el momento en el que la credencial fue verificada correctamente.</summary>
    Task MarkVerifiedAsync(
        int cameraId,
        DateTimeOffset verifiedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Elimina la asociación de credencial de la cámara.</summary>
    Task DeleteAsync(
        int cameraId,
        CancellationToken cancellationToken = default);
}
