using CameraInspector.Core.Models;
using LibVLCSharp.Shared;

namespace CameraInspector.Video;

/// <summary>
/// Contrato de alto nivel para reproducir streams de video dentro de la aplicación.
/// La UI consume este contrato sin conocer detalles de inicialización de LibVLC.
/// </summary>
public interface IVideoPlayerService : IDisposable
{
    /// <summary>MediaPlayer que debe asociarse al VideoView de WPF.</summary>
    MediaPlayer Player { get; }

    /// <summary>Abre y reproduce el stream RTSP indicado.</summary>
    void Play(CameraStreamInfo stream);

    /// <summary>Detiene la reproducción actual.</summary>
    void Stop();
}
