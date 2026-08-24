using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Resuelve la URL RTSP real de un dispositivo, consultando su Media Service ONVIF
/// (GetProfiles + GetStreamUri). Es el contrato de la Capa 5 que la Capa 7 (Video) consume
/// para saber A QUÉ conectarse — nunca arma la URL "a mano" con formatos propietarios,
/// eso es tarea de los providers de fabricante más adelante.
/// </summary>
public interface IStreamUriResolver
{
    /// <summary>
    /// Devuelve la URL del stream principal (primer perfil, o el que tenga mayor resolución
    /// si hay varios). Null si el dispositivo no expone Media Service o las credenciales fallan.
    /// </summary>
    Task<CameraStreamInfo?> GetMainStreamUriAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);
}
