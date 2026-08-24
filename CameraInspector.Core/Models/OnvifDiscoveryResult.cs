namespace CameraInspector.Core.Models;

/// <summary>
/// Resultado de un Probe WS-Discovery respondido por un dispositivo ONVIF.
/// Este modelo representa la información de descubrimiento antes de convertirla
/// en un DiscoveredDevice completo.
/// </summary>
public sealed record OnvifDiscoveryResult
{
    /// <summary>
    /// URI única anunciada por WS-Discovery para identificar al dispositivo.
    /// Puede utilizarse para deduplicar respuestas del mismo equipo.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// URL del Device Service ONVIF anunciada dentro de ProbeMatch/XAddrs.
    /// Es el endpoint que las capas posteriores deben reutilizar.
    /// </summary>
    public required string DeviceServiceXAddr { get; init; }

    /// <summary>
    /// Tipos WS-Discovery publicados por el dispositivo, por ejemplo
    /// tds:Device o dn:NetworkVideoTransmitter.
    /// </summary>
    public string? Types { get; init; }

    /// <summary>
    /// Scopes publicados por el dispositivo; normalmente contienen pistas
    /// sobre nombre, hardware, ubicación o tipo de dispositivo.
    /// </summary>
    public string? Scopes { get; init; }
}