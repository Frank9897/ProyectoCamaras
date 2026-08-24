namespace CameraInspector.Core.Models;

/// <summary>
/// Información completa de un stream de video resuelto mediante ONVIF Media Service.
/// La Capa 7 utilizará este modelo para abrir el stream con FFmpeg u otro reproductor.
/// </summary>
public sealed record CameraStreamInfo
{
    /// <summary>URI RTSP final que el dispositivo entrega para este perfil.</summary>
    public required string RtspUri { get; init; }

    /// <summary>Token ONVIF utilizado para solicitar la URI del perfil.</summary>
    public required string ProfileToken { get; init; }

    /// <summary>Nombre legible del perfil, si el dispositivo lo proporciona.</summary>
    public string? ProfileName { get; init; }

    /// <summary>Ancho del video en píxeles reportado por VideoEncoderConfiguration.</summary>
    public int? Width { get; init; }

    /// <summary>Alto del video en píxeles reportado por VideoEncoderConfiguration.</summary>
    public int? Height { get; init; }

    /// <summary>Codec de video reportado por ONVIF, por ejemplo H264, H265 o JPEG.</summary>
    public string? Encoding { get; init; }

    /// <summary>Límite de FPS configurado para el perfil.</summary>
    public int? FrameRate { get; init; }

    /// <summary>
    /// Indica si el perfil fue seleccionado como stream principal.
    /// False representa un stream secundario cuando se solicita explícitamente.
    /// </summary>
    public bool IsMainStream { get; init; }

    /// <summary>Descripción compacta de la resolución para mostrarla fácilmente en la UI.</summary>
    public string Resolution =>
        Width.HasValue && Height.HasValue
            ? $"{Width}x{Height}"
            : "Desconocida";
}
