namespace CameraInspector.Core.Models;

/// <summary>
/// Evento ONVIF recibido o consultado desde una cámara.
/// Se mantiene genérico para poder representar movimiento, entradas digitales y eventos propietarios.
/// </summary>
public sealed record OnvifEventInfo
{
    public DateTimeOffset? Time { get; init; }
    public string? Topic { get; init; }
    public string? Source { get; init; }
    public string? Data { get; init; }
}
