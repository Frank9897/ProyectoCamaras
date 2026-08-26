using System.Runtime.InteropServices;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using DirectShowLib;
using LibVLCSharp.Shared;

namespace CameraInspector.Video;

/// <summary>
/// Implementación de cámaras locales mediante DirectShow + LibVLC.
/// DirectShow enumera fuentes de vídeo que Windows expone localmente y LibVLC abre la fuente seleccionada.
/// </summary>
public sealed class LocalCameraService : ILocalCameraService
{
    private readonly LibVLC _libVlc;
    private Media? _currentMedia;
    private MediaPlayer? _currentPlayer;

    /// <summary>Notifica a la ventana qué MediaPlayer debe mostrar o retirar.</summary>
    public event EventHandler<MediaPlayer?>? PlayerChanged;

    /// <summary>Reproductor local actualmente activo.</summary>
    public MediaPlayer? Player => _currentPlayer;

    public LocalCameraService()
    {
        // Inicializamos el motor multimedia local para capturas DirectShow.
        global::LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC("--quiet", "--live-caching=100");
    }

    public IReadOnlyList<LocalCameraDevice> GetAvailableCameras()
    {
        var cameras = new List<LocalCameraDevice>();

        try
        {
            // devices contiene las fuentes de vídeo registradas en la categoría VideoInputDevice de DirectShow.
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            foreach (var device in devices)
            {
                try
                {
                    // name es el nombre amigable utilizado por Windows para la fuente de vídeo.
                    var name = device.Name;
                    // devicePath es un identificador único de captura cuando el driver lo proporciona.
                    var devicePath = device.DevicePath;
                    // monikerString sirve como referencia técnica del moniker COM para diagnóstico.
                    var monikerString = device.Mon?.ToString();

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    cameras.Add(new LocalCameraDevice
                    {
                        Name = name,
                        DevicePath = devicePath,
                        MonikerString = monikerString,
                        IsVideoCaptureDevice = true,
                        Status = "Disponible"
                    });
                }
                catch
                {
                    // Un driver problemático no debe impedir detectar las demás cámaras locales.
                }
                finally
                {
                    // Liberamos la referencia COM del dispositivo después de copiar sus datos.
                    ReleaseComObject(device);
                }
            }
        }
        catch
        {
            // Un fallo de DirectShow se traduce en una lista vacía y no bloquea el resto de la aplicación.
        }

        return cameras
            .OrderBy(camera => camera.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool Play(LocalCameraDevice camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        // Una única captura local activa simplifica el control del hardware y evita conflictos de driver.
        Stop();

        // dshow:// es el origen de captura DirectShow de VLC en Windows.
        _currentMedia = new Media(_libVlc, new Uri("dshow://"), FromType.FromLocation);
        // dshow-vdev selecciona la fuente por FriendlyName/Name registrado en DirectShow.
        _currentMedia.AddOption($":dshow-vdev={EscapeOption(camera.Name)}");
        // En esta fase solo necesitamos vídeo; no abrimos un micrófono asociado automáticamente.
        _currentMedia.AddOption(":dshow-adev=none");
        // live-caching pequeño para reducir la latencia de la vista previa local.
        _currentMedia.AddOption(":live-caching=100");

        _currentPlayer = new MediaPlayer(_libVlc);

        // started indica si LibVLC aceptó la creación de la fuente DirectShow.
        var started = _currentPlayer.Play(_currentMedia);
        if (!started)
        {
            Stop();
            return false;
        }

        PlayerChanged?.Invoke(this, _currentPlayer);
        return true;
    }

    public void Stop()
    {
        if (_currentPlayer is not null)
        {
            // IsPlaying indica si la captura local continúa activa.
            if (_currentPlayer.IsPlaying)
                _currentPlayer.Stop();

            _currentPlayer.Dispose();
            _currentPlayer = null;
        }

        // Liberamos el medio nativo después de cerrar el MediaPlayer.
        _currentMedia?.Dispose();
        _currentMedia = null;

        PlayerChanged?.Invoke(this, null);
    }

    public void Dispose()
    {
        Stop();
        _libVlc.Dispose();
    }

    private static string EscapeOption(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void ReleaseComObject(object instance)
    {
        if (!Marshal.IsComObject(instance))
            return;

        try
        {
            Marshal.FinalReleaseComObject(instance);
        }
        catch
        {
            // Ignoramos fallos aislados de liberación COM provocados por drivers no estándar.
        }
    }
}
