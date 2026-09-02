namespace CameraInspector.Core.Models;

/// <summary>
/// Estado operativo de una cámara detectada.
/// </summary>
public enum CameraHealthState
{
    Unknown,
    Healthy,
    CommunicationOnly,
    AuthenticationRequired,
    NoVideo,
    NoResponse,
    Degraded,
    Unsupported
}

/// <summary>
/// Resultado de una comprobación ligera de comunicación y vídeo.
/// No sustituye al diagnóstico completo.
/// </summary>
public sealed record CameraHealthResult
{
    public CameraHealthState State { get; init; } = CameraHealthState.Unknown;
    public bool CommunicationAvailable { get; init; }
    public bool VideoAvailable { get; init; }
    public bool AuthenticationRequired { get; init; }
    public int? CommunicationPort { get; init; }
    public string? Protocol { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
}
