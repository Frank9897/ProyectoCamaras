using System.Runtime.InteropServices;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using DirectShowLib;
using LibVLCSharp.Shared;

namespace CameraInspector.Video;

/// <summary>
/// Implementación de cámaras locales mediante DirectShow + LibVLC.
/// DirectShow permite enumerar los dispositivos de vídeo que Windows expone como fuentes de captura;
/// LibVLC reutiliza el mismo motor multimedia utilizado por las cámaras IP.
/// </summary>
public sealed class LocalCameraService : ILocalCameraService
{
    private readonly LibVLC _libVlc;
    private Media? _currentMedia;
    private MediaPlayer? _currentPlayer;

    /// <summary>
    /// Evento que permite a la UI reemplazar temporalmente el reproductor visible por el reproductor local.
    /// </summary>
    public event EventHandler<MediaPlayer?>? PlayerChanged;

    /// <summary>
    /// Reproductor activo para la cámara local seleccionada.
    /// </summary>
    public MediaPlayer? Player => _currentPlayer;

    public LocalCameraService()
    {
        // La aplicación ya inicializa LibVLC en el servicio principal; esta instancia comparte únicamente la tecnología.
        // Se inicializa aquí también para que el servicio pueda utilizarse de manera independiente en tests o ventanas futuras.
        global::LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC("--quiet", "--live-caching=100");
    }

    public IReadOnlyList<LocalCameraDevice> GetAvailableCameras()
    {
        var cameras = new List<LocalCameraDevice>();
        DsDevice[]? devices = null;

        try
        {
            // devices contiene las fuentes de vídeo DirectShow registradas por Windows.
            devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            foreach (var device in devices)
            {
                try
                {
                    // Name es el nombre amigable que también utiliza normalmente el módulo dshow de LibVLC.
                    var name = device.Name;
                    // DevicePath permite conservar una identidad más estable aunque el nombre amigable cambie.
                    var path = TryGetDevicePath(device);

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    cameras.Add(new LocalCameraDevice
                    {
                        Name = name,
                        DevicePath = path,
                        MonikerString = TryGetMonikerString(device),
                        IsVideoCaptureDevice = true,
                        Status = "Disponible"
                    });
                }
                catch
                {
                    // Un dispositivo defectuoso no debe impedir enumerar las demás webcams.
                }
                finally
                {
                    // DirectShow utiliza COM; liberamos cada moniker tras copiar la información necesaria.
                    ReleaseComObject(device);
                }
            }
        }
        catch
        {
            // Si DirectShow no está disponible o un driver falla, devolvemos una lista vacía sin tumbar la app.
        }

        return cameras
            .OrderBy(camera => camera.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool Play(LocalCameraDevice camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        Stop();

        // dshow:// es la fuente de entrada local de DirectShow dentro de LibVLC.
        _currentMedia = new Media(_libVlc, new Uri("dshow://"), FromType.FromLocation);

        // dshow-vdev selecciona la webcam por el nombre que Windows expone a DirectShow.
        _currentMedia.AddOption($":dshow-vdev={EscapeOption(camera.Name)}");
        // No abrimos micrófono de forma implícita: el objetivo de esta fase es vídeo local.
        _currentMedia.AddOption(":dshow-adev=none");
        // live-caching pequeño reduce la latencia de una webcam frente a una reproducción de archivo.
        _currentMedia.AddOption(":live-caching=100");
        // Evitamos que LibVLC abra una ventana propia; la UI WPF seguirá controlando el destino del vídeo.
        _currentMedia.AddOption(":no-video-title-show");

        _currentPlayer = new MediaPlayer(_libVlc);

        // started indica si LibVLC aceptó el inicio de la captura local.
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
            // IsPlaying indica si la fuente DirectShow permanece activa.
            if (_currentPlayer.IsPlaying)
                _currentPlayer.Stop();

            _currentPlayer.Dispose();
            _currentPlayer = null;
        }

        // El Media nativo debe liberarse después del MediaPlayer que lo estaba utilizando.
        _currentMedia?.Dispose();
        _currentMedia = null;

        PlayerChanged?.Invoke(this, null);
    }

    public void Dispose()
    {
        Stop();
        _libVlc.Dispose();
    }

    private static string? TryGetDevicePath(DsDevice device)
    {
        try
        {
            // DevicePath existe en versiones de DirectShowLib.Net que exponen la propiedad del dispositivo.
            return string.IsNullOrWhiteSpace(device.DevicePath) ? null : device.DevicePath;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetMonikerString(DsDevice device)
    {
        try
        {
            // MonikerString sirve como identificador técnico para diagnósticos, no se muestra como dato principal al usuario.
            return device.MonikerString;
        }
        catch
        {
            return null;
        }
    }

    private static string EscapeOption(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void ReleaseComObject(object instance)
    {
        if (Marshal.IsComObject(instance))
        {
            try
            {
                Marshal.FinalReleaseComObject(instance);
            }
            catch
            {
                // Algunos drivers no exponen correctamente el ciclo COM; no propagamos ese fallo a la UI.
            }
        }
    }
}
