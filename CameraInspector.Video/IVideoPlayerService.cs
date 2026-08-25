using CameraInspector.Core.Models;
using LibVLCSharp.Shared;

namespace CameraInspector.Video;

/// <summary>
/// Contrato de alto nivel para reproducir y grabar streams dentro de la aplicación.
/// La UI no necesita conocer la inicialización ni administración de recursos de LibVLC.
/// </summary>
public interface IVideoPlayerService : IDisposable
{
    /// <summary>MediaPlayer que debe asociarse al VideoView de WPF para la reproducción visible.</summary>
    MediaPlayer Player { get; }

    /// <summary>
    /// Indica si existe una grabación RTSP activa en el reproductor secundario.
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Abre y reproduce un stream RTSP.
    /// Las credenciales se pasan por separado para no guardarlas dentro del modelo del stream.
    /// </summary>
    void Play(CameraStreamInfo stream, string? username, string? password);

    /// <summary>Detiene la reproducción visible actual.</summary>
    void Stop();

    /// <summary>
    /// Captura el frame actual a PNG.
    /// Devuelve false cuando LibVLC todavía no dispone de una salida de video activa.
    /// </summary>
    bool TakeSnapshot(string filePath, uint width = 0, uint height = 0);

    /// <summary>
    /// Inicia una segunda reproducción LibVLC dedicada a guardar el stream RTSP en disco.
    /// </summary>
    bool StartRecording(CameraStreamInfo stream, string? username, string? password, string filePath);

    /// <summary>Detiene y libera la grabación RTSP activa.</summary>
    void StopRecording();
}
