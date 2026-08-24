using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Resuelve las URL RTSP reales de un dispositivo, consultando su Media Service ONVIF.
/// La UI y la futura Capa 7 de video consumen este contrato sin conocer detalles SOAP.
/// </summary>
public interface IStreamUriResolver
{
    /// <summary>
    /// Devuelve el stream principal, normalmente el perfil con mayor resolución disponible.
    /// </summary>
    Task<CameraStreamInfo?> GetMainStreamUriAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve el stream secundario, normalmente el perfil con menor resolución disponible.
    /// </summary>
    Task<CameraStreamInfo?> GetSubStreamUriAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);
}
