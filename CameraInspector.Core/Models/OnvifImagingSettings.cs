namespace CameraInspector.Core.Models;

/// <summary>
/// Ajustes de imagen normalizados que CameraInspector puede leer/escribir mediante ONVIF.
/// Las propiedades son nullable porque una cámara puede no exponer alguna capacidad.
/// </summary>
public sealed record OnvifImagingSettings
{
    public float? Brightness { get; init; }
    public float? ColorSaturation { get; init; }
    public float? Contrast { get; init; }
    public float? Sharpness { get; init; }
    public string? IrCutFilter { get; init; }
}
