using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato para enumerar y controlar cámaras de vídeo locales expuestas por Windows.
/// La implementación concreta pertenece a la capa Video para mantener Core independiente de DirectShow/Media Foundation.
/// </summary>
public interface ILocalCameraService : IDisposable
{
    /// <summary>
    /// Enumera los dispositivos de captura de vídeo disponibles en el equipo.
    /// </summary>
    IReadOnlyList<LocalCameraDevice> GetAvailableCameras();

    /// <summary>
    /// Inicia la captura de la cámara local seleccionada en el reproductor visible.
    /// </summary>
    bool Play(LocalCameraDevice camera);

    /// <summary>
    /// Detiene la captura local actualmente activa.
    /// </summary>
    void Stop();
}
