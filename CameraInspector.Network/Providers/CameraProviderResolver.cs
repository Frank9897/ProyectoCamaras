using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers;

/// <summary>
/// Selecciona el primer provider propietario que pueda operar sobre el dispositivo.
/// La resolución se mantiene independiente de la UI.
/// </summary>
public sealed class CameraProviderResolver : ICameraProviderResolver
{
    private readonly IReadOnlyList<ICameraProvider> _providers;

    public CameraProviderResolver(IEnumerable<ICameraProvider> providers)
    {
        // _providers conserva el orden de registro para que el resultado sea determinista.
        _providers = providers.ToList();
    }

    /// <summary>Busca un provider compatible sin realizar llamadas de red.</summary>
    public ICameraProvider? Resolve(DiscoveredDevice device)
    {
        // provider es el primer protocolo propietario cuya evidencia actual coincide con el dispositivo.
        return _providers.FirstOrDefault(provider => provider.CanHandle(device));
    }
}
