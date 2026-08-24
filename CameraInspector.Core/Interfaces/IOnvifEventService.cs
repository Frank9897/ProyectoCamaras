using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Acceso a eventos ONVIF de una cámara. La primera implementación usa PullMessages
/// para evitar mantener un listener HTTP abierto en el equipo técnico.
/// </summary>
public interface IOnvifEventService
{
    Task<IReadOnlyList<OnvifEventInfo>> PullMessagesAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        int timeoutSeconds = 5,
        int messageLimit = 20,
        CancellationToken cancellationToken = default);
}
