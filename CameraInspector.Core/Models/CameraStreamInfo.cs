namespace CameraInspector.Core.Models;

/// <summary>
/// Resultado de resolver la URL real de un stream de video en un dispositivo ONVIF,
/// vía el Media Service (Capa 5). Es lo que la Capa 7 (Video/FFmpeg) va a necesitar
/// para poder abrir la conexión RTSP real.
/// </summary>
public sealed record CameraStreamInfo
{
    public required string RtspUri { get; init; }
    public required string ProfileToken { get; init; }
    public string? ProfileName { get; init; }
    public bool IsMainStream { get; init; }
}
