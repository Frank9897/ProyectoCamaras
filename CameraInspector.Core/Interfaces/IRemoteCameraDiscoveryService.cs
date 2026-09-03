using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Permite resolver cámaras a través de un punto de entrada remoto (VMS, NVR/DVR,
/// proxy o servicio propietario) sin acoplar el dominio a un fabricante concreto.
/// </summary>
public interface IRemoteCameraDiscoveryService
{
    Task<RemoteConnectionResult> ProbeAsync(
        RemoteConnectionTarget target,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        RemoteConnectionTarget target,
        CancellationToken cancellationToken = default);
}

public sealed record RemoteConnectionResult(
    bool Connected,
    string Protocol,
    string Message,
    string? ServerName = null,
    bool SupportsCameraEnumeration = false);
