namespace CameraInspector.Core.Models;

/// <summary>
/// Punto de entrada remoto utilizado por servicios de cámaras, VMS, NVR/DVR o proxies.
/// El endpoint no se asocia a un fabricante: el protocolo real se determina durante la conexión.
/// </summary>
public sealed record RemoteConnectionTarget
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string Protocol { get; init; } = "AUTO";
}
