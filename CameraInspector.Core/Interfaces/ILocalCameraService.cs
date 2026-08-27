using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato para enumerar y controlar cámaras de vídeo locales expuestas por Windows.
/// La implementación concreta pertenece a la capa Video para mantener Core independiente de DirectShow/LibVLC.
/// </summary>
public interface ILocalCameraService : IDisposable
{
    /// <summary>Enumera los dispositivos de captura de vídeo disponibles en el equipo.</summary>
    IReadOnlyList<LocalCameraDevice> GetAvailableCameras();

    /// <summary>
    /// Inicia la previsualización y espera a que LibVLC cree una salida de vídeo real.
    /// </summary>
    Task<bool> PlayAsync(LocalCameraDevice camera, CancellationToken cancellationToken = default);

    /// <summary>Detiene la previsualización local actualmente activa.</summary>
    void Stop();

    /// <summary>
    /// Captura un snapshot del vídeo local activo en la ruta indicada.
    /// </summary>
    bool TakeSnapshot(string filePath);

    /// <summary>
    /// Inicia una grabación local independiente de la previsualización.
    /// </summary>
    bool StartRecording(string filePath);

    /// <summary>Detiene la grabación local activa y libera sus recursos nativos.</summary>
    void StopRecording();

    /// <summary>Indica si existe una grabación local activa.</summary>
    bool IsRecording { get; }

    /// <summary>Indica la cantidad de salidas de vídeo activas en la previsualización.</summary>
    uint VideoOutputCount { get; }
}
