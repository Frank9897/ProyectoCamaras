using CameraInspector.Core.Models;
using LibVLCSharp.Shared;

namespace CameraInspector.Video;

/// <summary>
/// Reproductor RTSP basado en LibVLCSharp.
/// Mantiene un reproductor visible para la vista previa y otro reproductor independiente para grabación.
/// </summary>
public sealed class LibVlcVideoPlayerService : IVideoPlayerService
{
    /// <summary>Instancia de LibVLC utilizada para crear medios y reproductores.</summary>
    private readonly LibVLC _libVlc;

    /// <summary>
    /// Medio RTSP actualmente asociado al reproductor visible.
    /// </summary>
    private Media? _currentMedia;

    /// <summary>
    /// MediaPlayer que entrega la reproducción visible al VideoView de WPF.
    /// </summary>
    public MediaPlayer Player { get; }

    /// <summary>
    /// Medio RTSP actualmente asociado al reproductor de grabación.
    /// </summary>
    private Media? _recordingMedia;

    /// <summary>
    /// Reproductor secundario que mantiene la conexión RTSP dedicada a la grabación.
    /// </summary>
    private MediaPlayer? _recordingPlayer;

    /// <summary>
    /// Indica si existe una grabación activa en el reproductor secundario.
    /// </summary>
    public bool IsRecording => _recordingPlayer?.IsPlaying == true;

    public LibVlcVideoPlayerService()
    {
        // Usamos el nombre totalmente calificado para evitar conflictos con el namespace CameraInspector.Core.
        global::LibVLCSharp.Shared.Core.Initialize();

        // _libVlc contiene el motor multimedia. El cache y RTSP-TCP priorizan estabilidad para CCTV.
        _libVlc = new LibVLC(
            "--quiet",
            "--network-caching=500",
            "--rtsp-tcp");

        // Player administra la reproducción visible actual.
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

        // _currentMedia representa la nueva fuente RTSP visible.
        _currentMedia = CreateRtspMedia(stream, username, password);

        // MediaPlayer.Play inicia la conexión RTSP y entrega el video al VideoView asociado.
        Player.Play(_currentMedia);
    }

    public void Stop()
    {
        // Player.IsPlaying indica si existe una reproducción visible activa.
        if (Player.IsPlaying)
            Player.Stop();

        // Liberamos el Media anterior después de detener el reproductor visible.
        _currentMedia?.Dispose();
        _currentMedia = null;
    }

    public bool TakeSnapshot(string filePath, uint width = 0, uint height = 0)
    {
        // ArgumentNullException evita pasar una ruta vacía al componente nativo de LibVLC.
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Snapshot requiere una salida de video activa.
        if (!Player.IsPlaying)
            return false;

        // LibVLCSharp devuelve true cuando la solicitud de captura fue aceptada por el reproductor.
        return Player.TakeSnapshot(0, filePath, width, height);
    }

    public bool StartRecording(
        CameraStreamInfo stream,
        string? username,
        string? password,
        string filePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // StopRecording evita dejar una grabación anterior usando recursos nativos.
        StopRecording();

        // _recordingMedia representa la misma fuente RTSP, pero con una salida sout hacia disco.
        _recordingMedia = CreateRtspMedia(stream, username, password);

        // destination es una ruta local explícitamente seleccionada por el técnico.
        var destination = EscapeSoutPath(filePath);

        // sout escribe el stream directamente en MPEG-TS sin transcodificar, reduciendo CPU y latencia.
        _recordingMedia.AddOption($":sout=#std{{access=file,mux=ts,dst=\"{destination}\"}}");
        // no-sout-display evita que el reproductor secundario intente renderizar una segunda ventana de video.
        _recordingMedia.AddOption(":no-sout-display");
        // sout-keep conserva la salida mientras la fuente permanezca activa.
        _recordingMedia.AddOption(":sout-keep");

        // _recordingPlayer es independiente del Player visible, por lo que la vista previa continúa funcionando.
        _recordingPlayer = new MediaPlayer(_libVlc);

        // started indica si LibVLC aceptó iniciar la reproducción dedicada a grabación.
        var started = _recordingPlayer.Play(_recordingMedia);

        if (!started)
        {
            StopRecording();
            return false;
        }

        return true;
    }

    public void StopRecording()
    {
        // Si existe un reproductor de grabación activo, lo detenemos antes de liberar el Media.
        if (_recordingPlayer is not null)
        {
            if (_recordingPlayer.IsPlaying)
                _recordingPlayer.Stop();

            _recordingPlayer.Dispose();
            _recordingPlayer = null;
        }

        // _recordingMedia debe liberarse después del reproductor que lo estaba consumiendo.
        _recordingMedia?.Dispose();
        _recordingMedia = null;
    }

    public void Dispose()
    {
        // La grabación tiene prioridad de liberación antes de destruir el motor LibVLC.
        StopRecording();
        Stop();

        // Player debe liberarse antes del motor LibVLC para cerrar correctamente recursos nativos.
        Player.Dispose();
        _libVlc.Dispose();
    }

    /// <summary>
    /// Crea un objeto Media RTSP con autenticación opcional.
    /// </summary>
    private Media CreateRtspMedia(
        CameraStreamInfo stream,
        string? username,
        string? password)
    {
        // media representa la fuente RTSP solicitada por la cámara.
        var media = new Media(_libVlc, stream.RtspUri, FromType.FromLocation);

        // La autenticación se pasa como opciones de LibVLC y no se persiste en CameraStreamInfo.
        if (!string.IsNullOrWhiteSpace(username))
            media.AddOption($":rtsp-user={EscapeOption(username)}");

        if (!string.IsNullOrWhiteSpace(password))
            media.AddOption($":rtsp-pwd={EscapeOption(password)}");

        return media;
    }

    /// <summary>
    /// Escapa mínimamente caracteres que podrían alterar el formato de una opción de LibVLC.
    /// </summary>
    private static string EscapeOption(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>
    /// Escapa una ruta Windows para que pueda viajar dentro de una opción sout delimitada por comillas.
    /// </summary>
    private static string EscapeSoutPath(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);
}
