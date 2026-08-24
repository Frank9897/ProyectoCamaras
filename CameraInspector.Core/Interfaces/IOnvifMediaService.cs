using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Acceso al Media Service ONVIF para consultar perfiles y resolver sus streams.
/// </summary>
public interface IOnvifMediaService
{
    Task<IReadOnlyList<OnvifMediaProfile>> GetProfilesAsync(
        DiscoveredDevice device,
        string mediaServiceXAddr,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);

    Task<string?> GetStreamUriAsync(
        string mediaServiceXAddr,
        string profileToken,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);
}