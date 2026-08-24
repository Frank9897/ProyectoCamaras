namespace CameraInspector.Core.Models;

/// <summary>
/// Representa la referencia de una credencial asociada a una cámara del inventario.
/// La contraseña nunca forma parte de este modelo; únicamente se conserva la referencia
/// al almacén seguro y el nombre de usuario necesario para mostrar el estado en la UI.
/// </summary>
public sealed record CameraCredentialInfo
{
    /// <summary>Identificador SQLite de la cámara propietaria de la credencial.</summary>
    public required int CameraId { get; init; }

    /// <summary>Nombre de usuario que se utilizará para autenticación.</summary>
    public required string Username { get; init; }

    /// <summary>Referencia que apunta al secreto almacenado en Windows Credential Manager.</summary>
    public required Guid CredentialRef { get; init; }

    /// <summary>Momento en que la credencial fue verificada correctamente por última vez.</summary>
    public DateTimeOffset? LastVerifiedAt { get; init; }
}
