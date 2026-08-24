using CameraInspector.Core.Models;
using LibVLCSharp.Shared;

namespace CameraInspector.Video;

/// <summary>
/// Reproductor RTSP basado en LibVLCSharp.
/// Se encarga exclusivamente del ciclo de vida de LibVLC, Media y MediaPlayer.
/// </summary>
public sealed class LibVlcVideoPlayerService : IVideoPlayerService
{
    /// <summary>Instancia global de LibVLC utilizada para crear medios y reproductores.</summary>
    private readonly LibVLC _libVlc;

    /// <summary>Instancia que controla la reproducción y entrega los frames al VideoView de WPF.</summary>
    public MediaPlayer Player { get; }

    public LibVlcVideoPlayerService()
    {
        // Initialize localiza las bibliotecas nativas incluidas por VideoLAN.LibVLC.Windows.
        Core.Initialize();

        // _libVlc contiene el motor multimedia. Los argumentos reducen buffering excesivo
        // y obligan a RTSP sobre TCP, que suele ser más estable para una herramienta de diagnóstico.
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

        // Stop libera la reproducción anterior antes de abrir otro stream.
        Stop();

        // media representa la fuente RTSP concreta que LibVLC va a abrir.
        using var media = new Media(_libVlc, stream.RtspUri, FromType.FromLocation);

        // La autenticación se pasa como opciones de LibVLC y no se persiste en el modelo.
        if (!string.IsNullOrWhiteSpace(username))
            media.AddOption($":rtsp-user={EscapeOption(username)}");

        if (!string.IsNullOrWhiteSpace(password))
            media.AddOption($":rtsp-pwd={EscapeOption(password)}");

        // MediaPlayer.Play inicia la conexión RTSP y entrega el video al VideoView asociado.
        Player.Play(media);
    }

    public void Stop()
    {
        // IsPlaying evita ejecutar Stop innecesariamente cuando no existe una reproducción activa.
        if (Player.IsPlaying)
            Player.Stop();
    }

    public void Dispose()
    {
        // Player debe liberarse antes del motor LibVLC para evitar recursos nativos pendientes.
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
