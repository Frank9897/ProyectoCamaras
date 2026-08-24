using CameraInspector.Core.Models;
using LibVLCSharp.Shared;

namespace CameraInspector.Video;

/// <summary>
/// Reproductor RTSP basado en LibVLCSharp.
/// Se encarga exclusivamente del ciclo de vida de LibVLC, Media y MediaPlayer.
/// </summary>
public sealed class LibVlcVideoPlayerService : IVideoPlayerService
{
    /// <summary>Instancia de LibVLC utilizada para crear medios y reproductores.</summary>
    private readonly LibVLC _libVlc;

    /// <summary>
    /// Medio RTSP actualmente asociado al reproductor.
    /// Se conserva mientras está activo para mantener correctamente su ciclo de vida nativo.
    /// </summary>
    private Media? _currentMedia;

    /// <summary>Instancia que controla la reproducción y entrega los frames al VideoView de WPF.</summary>
    public MediaPlayer Player { get; }

    public LibVlcVideoPlayerService()
    {
        // Usamos el nombre totalmente calificado para evitar que el namespace CameraInspector.Core
        // sea interpretado como si contuviera el método Initialize.
        global::LibVLCSharp.Shared.Core.Initialize();

        // _libVlc contiene el motor multimedia. El cache y RTSP-TCP priorizan estabilidad
        // para redes CCTV frente a una latencia mínima.
        _libVlc = new LibVLC(
            "--quiet",
            "--network-caching=500",
            "--rtsp-tcp");

        // Player administra la reproducción actual y queda expuesto para enlazarlo al VideoView.
        Player = new MediaPlayer(_libVlc);
    }

    public void Play(
        CameraStreamInfo stream,
        string? username,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Stop detiene la reproducción anterior y libera el Media nativo anterior.
        Stop();

        // _currentMedia representa la nueva fuente RTSP y se mantiene viva mientras se reproduce.
        _currentMedia = new Media(_libVlc, stream.RtspUri, FromType.FromLocation);

        // La autenticación se pasa como opciones de LibVLC y no se persiste en CameraStreamInfo.
        if (!string.IsNullOrWhiteSpace(username))
            _currentMedia.AddOption($":rtsp-user={EscapeOption(username)}");

        if (!string.IsNullOrWhiteSpace(password))
            _currentMedia.AddOption($":rtsp-pwd={EscapeOption(password)}");

        // MediaPlayer.Play inicia la conexión RTSP y entrega el video al VideoView asociado.
        Player.Play(_currentMedia);
    }

    public void Stop()
    {
        // IsPlaying indica si existe una reproducción activa que deba detenerse.
        if (Player.IsPlaying)
            Player.Stop();

        // Liberamos el Media anterior después de detener el reproductor.
        _currentMedia?.Dispose();
        _currentMedia = null;
    }

    public bool TakeSnapshot(string filePath, uint width = 0, uint height = 0)
    {
        // ArgumentNullException evita pasar una ruta vacía al componente nativo de LibVLC.
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Snapshot requiere una salida de video activa; sin reproducción no existe frame que capturar.
        if (!Player.IsPlaying)
            return false;

        // LibVLC devuelve 0 cuando la solicitud de snapshot pudo iniciarse correctamente.
        var result = Player.TakeSnapshot(0, filePath, width, height);
        return result == 0;
    }

    public void Dispose()
    {
        // Stop libera el medio actualmente abierto antes de destruir Player y LibVLC.
        Stop();

        // Player debe liberarse antes del motor LibVLC para cerrar correctamente recursos nativos.
        Player.Dispose();
        _libVlc.Dispose();
    }

    /// <summary>
    /// Escapa mínimamente caracteres que podrían alterar el formato de una opción de LibVLC.
    /// </summary>
    private static string EscapeOption(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);
}
