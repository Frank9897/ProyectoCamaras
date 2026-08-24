using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato para leer y modificar ajustes básicos de Imaging mediante ONVIF.
/// La implementación concreta conoce SOAP; la UI solo conoce este contrato.
/// </summary>
public interface IOnvifImagingService
{
    Task<OnvifImagingSettings?> GetImagingSettingsAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);

    Task<bool> SetImagingSettingsAsync(
        DiscoveredDevice device,
        OnvifImagingSettings settings,
        string? username,
        string? password,
        CancellationToken cancellationToken = default);
}
