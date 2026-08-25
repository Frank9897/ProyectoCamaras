using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato común para protocolos específicos de fabricantes.
/// Los providers amplían ONVIF; no reemplazan descubrimiento, inventario ni credenciales.
/// </summary>
public interface ICameraProvider
{
    /// <summary>Nombre estable del provider, por ejemplo Hikvision ISAPI.</summary>
    string Name { get; }

    /// <summary>
    /// Indica si el provider puede operar sobre el dispositivo descubierto sin realizar autenticación.
    /// Debe basarse solamente en evidencia ya disponible.
    /// </summary>
    bool CanHandle(DiscoveredDevice device);

    /// <summary>
    /// Obtiene información propietaria de lectura usando las credenciales proporcionadas por el usuario.
    /// </summary>
    Task<CameraProviderInfo?> GetDeviceInfoAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default);
}
