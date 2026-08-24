using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Acceso al Media Service ONVIF para consultar perfiles y resolver sus streams.
/// </summary>
public interface IOnvifMediaService
{
    /// <summary>Obtiene todos los perfiles de video publicados por el Media Service.</summary>
    Task<IReadOnlyList<OnvifMediaProfile>> GetProfilesAsync(
        DiscoveredDevice device,
        string mediaServiceXAddr,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>Obtiene la URI RTSP exacta de un perfil concreto.</summary>
    Task<string?> GetStreamUriAsync(
        string mediaServiceXAddr,
        string profileToken,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selecciona el perfil de mayor resolución disponible y resuelve su stream principal.
    /// </summary>
    Task<CameraStreamInfo?> GetMainStreamUriAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selecciona el perfil de menor resolución disponible y resuelve el stream secundario.
    /// </summary>
    Task<CameraStreamInfo?> GetSubStreamUriAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);
}
