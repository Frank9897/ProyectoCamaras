namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato para almacenar y recuperar credenciales sin exponer la contraseña
/// en las entidades de SQLite ni en los modelos de dominio.
/// </summary>
public interface ICredentialStore
{
    /// <summary>
    /// Guarda una credencial y devuelve una referencia estable que puede persistirse
    /// en SQLite sin guardar el secreto directamente.
    /// </summary>
    Task<Guid> SaveAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recupera una credencial mediante la referencia almacenada por la aplicación.
    /// </summary>
    Task<StoredCredential?> GetAsync(
        Guid credentialRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina una credencial del almacén seguro de Windows.
    /// </summary>
    Task DeleteAsync(
        Guid credentialRef,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Representa una credencial recuperada desde el almacén seguro.
/// La contraseña solo existe en memoria mientras la operación la necesita.
/// </summary>
public sealed record StoredCredential(
    string Username,
    string Password);
