using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato para enumerar y controlar cámaras de vídeo locales expuestas por Windows.
/// La implementación concreta pertenece a la capa Video para mantener Core independiente de OpenCV/DirectShow/LibVLC.
/// </summary>
public interface ILocalCameraService : IDisposable
{
    /// <summary>Enumera los dispositivos de captura de vídeo disponibles en el equipo.</summary>
    IReadOnlyList<LocalCameraDevice> GetAvailableCameras();

    /// <summary>Inicia la captura local y entrega frames BGRA mediante FrameReady.</summary>
    Task<bool> StartAsync(LocalCameraDevice camera, CancellationToken cancellationToken = default);

    /// <summary>Evento emitido cada vez que existe un frame válido de la cámara local.</summary>
    event EventHandler<LocalCameraFrame>? FrameReady;

    /// <summary>Detiene la captura local actualmente activa.</summary>
    void Stop();

    /// <summary>Captura el último frame disponible y lo guarda en la ruta indicada.</summary>
    bool TakeSnapshot(string filePath);

    /// <summary>Inicia una grabación local independiente de la vista previa.</summary>
    bool StartRecording(string filePath);

    /// <summary>Detiene la grabación local activa y libera sus recursos.</summary>
    void StopRecording();

    /// <summary>Indica si existe una grabación local activa.</summary>
    bool IsRecording { get; }

    /// <summary>Indica si existe una captura local activa.</summary>
    bool IsCapturing { get; }

    /// <summary>Información técnica de la última operación de captura.</summary>
    string LastCaptureDiagnostic { get; }
}
