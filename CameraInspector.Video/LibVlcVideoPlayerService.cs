using CameraInspector.Core.Models;
using LibVLCSharp.Shared;

namespace CameraInspector.Video;

/// <summary>
/// Reproductor RTSP basado en LibVLCSharp.
/// Mantiene un reproductor visible para la vista previa y otro reproductor independiente para grabación.
/// </summary>
public sealed class LibVlcVideoPlayerService : IVideoPlayerService
{
    private readonly LibVLC _libVlc;
    private Media? _currentMedia;
    public MediaPlayer Player { get; }
    private Media? _recordingMedia;
    private MediaPlayer? _recordingPlayer;

    public bool IsRecording => _recordingPlayer?.IsPlaying == true;

    public LibVlcVideoPlayerService()
    {
        global::LibVLCSharp.Shared.Core.Initialize();

        _libVlc = new LibVLC(
            "--quiet",
            "--network-caching=500",
            "--rtsp-tcp");

        Player = new MediaPlayer(_libVlc);
    }

    public void Play(
        CameraStreamInfo stream,
        string? username,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Stop();
        _currentMedia = CreateRtspMedia(stream, username, password);
        Player.Play(_currentMedia);
    }

    public void Stop()
    {
        if (Player.IsPlaying)
            Player.Stop();

        _currentMedia?.Dispose();
        _currentMedia = null;
    }

    public bool TakeSnapshot(string filePath, uint width = 0, uint height = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!Player.IsPlaying)
            return false;

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

        StopRecording();

        _recordingMedia = CreateRtspMedia(stream, username, password);
        var destination = EscapeSoutPath(filePath);

        // MP4 é mais amigável para Windows e players comuns. O stream é apenas remuxado,
        // sem transcodificação, para manter baixo o consumo de CPU.
        _recordingMedia.AddOption($":sout=#std{{access=file,mux=mp4,dst=\"{destination}\"}}");
        _recordingMedia.AddOption(":no-sout-display");
        _recordingMedia.AddOption(":sout-keep");

        _recordingPlayer = new MediaPlayer(_libVlc);
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
        if (_recordingPlayer is not null)
        {
            if (_recordingPlayer.IsPlaying)
                _recordingPlayer.Stop();

            _recordingPlayer.Dispose();
            _recordingPlayer = null;
        }

        _recordingMedia?.Dispose();
        _recordingMedia = null;
    }

    public void Dispose()
    {
        StopRecording();
        Stop();
        Player.Dispose();
        _libVlc.Dispose();
    }

    private Media CreateRtspMedia(
        CameraStreamInfo stream,
        string? username,
        string? password)
    {
        var media = new Media(_libVlc, stream.RtspUri, FromType.FromLocation);

        if (!string.IsNullOrWhiteSpace(username))
            media.AddOption($":rtsp-user={EscapeOption(username)}");

        if (!string.IsNullOrWhiteSpace(password))
            media.AddOption($":rtsp-pwd={EscapeOption(password)}");

        return media;
    }

    private static string EscapeOption(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string EscapeSoutPath(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);
}
