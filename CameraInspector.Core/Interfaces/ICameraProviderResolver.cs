using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Resuelve el provider propietario compatible con un dispositivo sin realizar autenticación.
/// </summary>
public interface ICameraProviderResolver
{
    /// <summary>Obtiene el provider compatible o null cuando no existe uno conocido.</summary>
    ICameraProvider? Resolve(DiscoveredDevice device);
}
