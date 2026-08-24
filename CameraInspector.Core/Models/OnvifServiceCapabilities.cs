namespace CameraInspector.Core.Models;

/// <summary>
/// Direcciones de los servicios ONVIF que la cámara anuncia mediante GetCapabilities.
/// XAddr es la URL real expuesta por el firmware y no debe inferirse a partir de la IP.
/// </summary>
public sealed class OnvifServiceCapabilities
{
    public string? DeviceServiceXAddr { get; init; }
    public string? MediaServiceXAddr { get; init; }
    public string? ImagingServiceXAddr { get; init; }
    public string? PtzServiceXAddr { get; init; }
    public string? EventsServiceXAddr { get; init; }

    public bool HasMediaService => !string.IsNullOrWhiteSpace(MediaServiceXAddr);
    public bool HasImagingService => !string.IsNullOrWhiteSpace(ImagingServiceXAddr);
    public bool HasPtzService => !string.IsNullOrWhiteSpace(PtzServiceXAddr);
    public bool HasEventsService => !string.IsNullOrWhiteSpace(EventsServiceXAddr);
}
