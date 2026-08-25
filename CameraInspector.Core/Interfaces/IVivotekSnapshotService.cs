namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato para solicitar un snapshot mediante la API propietaria de VIVOTEK.
/// La operación es explícita y requiere credenciales proporcionadas por el técnico.
/// </summary>
public interface IVivotekSnapshotService
{
    /// <summary>
    /// Descarga una imagen JPEG desde el CGI de snapshot de VIVOTEK.
    /// </summary>
    Task<bool> SaveSnapshotAsync(
        string ipAddress,
        string username,
        string password,
        string filePath,
        int? channel = null,
        int? resolution = null,
        int? quality = null,
        CancellationToken cancellationToken = default);
}
