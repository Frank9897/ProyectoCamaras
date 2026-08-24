namespace CameraInspector.Core.Models;

/// <summary>
/// Perfil de video expuesto por ONVIF Media Service.
/// Contiene solo los datos que CameraInspector necesita para seleccionar y diagnosticar streams.
/// </summary>
public sealed record OnvifMediaProfile
{
    public required string Token { get; init; }
    public string? Name { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Encoding { get; init; }
    public int? FrameRate { get; init; }

    public long ResolutionPixels => (long)(Width ?? 0) * (Height ?? 0);
}