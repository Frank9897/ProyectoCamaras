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

    /// <summary>Dirección IPv4/IPv6 detectada para el dispositivo en el momento del escaneo.</summary>
    public required string IpAddress { get; set; }

    /// <summary>Dirección MAC aprendida mediante ARP cuando está disponible.</summary>
    public string? MacAddress { get; set; }

    /// <summary>Nombre DNS/NetBIOS conocido, si la red lo proporciona.</summary>
    public string? Hostname { get; set; }

    /// <summary>Fabricante identificado por alguno de los detectores de la Capa 4.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>Modelo reportado por el dispositivo, normalmente mediante ONVIF o HTTP.</summary>
    public string? Model { get; set; }

    /// <summary>Versión de firmware reportada por el dispositivo.</summary>
    public string? FirmwareVersion { get; set; }

    /// <summary>Número de serie reportado por el dispositivo cuando el protocolo lo permite.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>Estado general derivado del último descubrimiento/diagnóstico.</summary>
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;

    /// <summary>Indica que se confirmó la presencia de un servicio ONVIF funcional.</summary>
    public bool OnvifSupported { get; set; }

    /// <summary>Perfil ONVIF identificado, por ejemplo S, T o G cuando podamos determinarlo.</summary>
    public string? OnvifProfile { get; set; }

    /// <summary>
    /// Dirección exacta del Device Service ONVIF descubierta durante la detección.
    /// Se conserva para que las siguientes capas reutilicen el XAddr real anunciado por el dispositivo.
    /// </summary>
    public string? OnvifDeviceServiceXAddr { get; set; }

    /// <summary>Indica que se detectó un servicio RTSP utilizable o anunciado.</summary>
    public bool RtspSupported { get; set; }

    /// <summary>Indica que se detectó un servicio HTTP utilizable.</summary>
    public bool HttpSupported { get; set; }

    /// <summary>Indica que se detectó un servicio HTTPS utilizable.</summary>
    public bool HttpsSupported { get; set; }

    /// <summary>Puerto HTTP detectado; queda nulo si todavía no fue identificado.</summary>
    public int? HttpPort { get; set; }

    /// <summary>Puerto RTSP detectado; queda nulo si todavía no fue identificado.</summary>
    public int? RtspPort { get; set; }

    /// <summary>Primera fecha/hora UTC en la que este objeto fue creado por el escaneo.</summary>
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Última fecha/hora UTC en la que el dispositivo fue visto por el escaneo.</summary>
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Nombre del provider asignado por la Capa 4 (ej. "HikvisionProvider", "GenericOnvifProvider").
    /// Nulo mientras el dispositivo no fue identificado todavía.
    /// </summary>
    public string? AssignedProviderName { get; set; }
}
