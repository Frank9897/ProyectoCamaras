namespace CameraInspector.Core.Models;

/// <summary>
/// Estado general del dispositivo, derivado del último diagnóstico o del propio descubrimiento.
/// </summary>
public enum DeviceStatus
{
    Unknown,
    Online,
    Warning,
    Error,
    Offline
}

/// <summary>
/// Representa un dispositivo detectado en la red durante la fase de descubrimiento (Capa 3),
/// antes o después de pasar por la resolución de fabricante (Capa 4).
/// Esta clase vive en Core porque es el contrato compartido entre Network, Onvif/Providers y Persistence.
/// </summary>
public sealed class DiscoveredDevice
{
    /// <summary>Identificador estable interno (no cambia aunque cambie la IP).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string IpAddress { get; set; }
    public string? MacAddress { get; set; }
    public string? Hostname { get; set; }

    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? SerialNumber { get; set; }

    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;

    public bool OnvifSupported { get; set; }
    public string? OnvifProfile { get; set; }
    public bool RtspSupported { get; set; }
    public bool HttpSupported { get; set; }
    public bool HttpsSupported { get; set; }

    public int? HttpPort { get; set; }
    public int? RtspPort { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Nombre del provider asignado por la Capa 4 (ej. "HikvisionProvider", "GenericOnvifProvider").
    /// Nulo mientras el dispositivo no fue identificado todavía.
    /// </summary>
    public string? AssignedProviderName { get; set; }
}
