namespace CameraInspector.Core.Models;

/// <summary>
/// Estado general del dispositivo, derivado del último descubrimiento/diagnóstico.
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
/// Representa un dispositivo detectado en la red durante el descubrimiento.
/// Contiene identidad, protocolos y endpoints que las distintas capas han podido confirmar.
/// </summary>
public sealed class DiscoveredDevice
{
    /// <summary>Identificador estable interno; no cambia aunque la IP cambie.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Dirección IP detectada para el dispositivo en el momento del escaneo.</summary>
    public required string IpAddress { get; set; }

    /// <summary>Dirección MAC aprendida mediante ARP cuando está disponible.</summary>
    public string? MacAddress { get; set; }

    /// <summary>Nombre DNS/NetBIOS conocido cuando la red lo proporciona.</summary>
    public string? Hostname { get; set; }

    /// <summary>Fabricante identificado por los detectores o por ONVIF.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>Modelo reportado por el dispositivo.</summary>
    public string? Model { get; set; }

    /// <summary>Versión de firmware reportada por el dispositivo.</summary>
    public string? FirmwareVersion { get; set; }

    /// <summary>Número de serie reportado por el dispositivo.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>Estado general mostrado por la aplicación.</summary>
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;

    /// <summary>Indica que se confirmó un servicio ONVIF funcional.</summary>
    public bool OnvifSupported { get; set; }

    /// <summary>Perfil ONVIF cuando podamos determinarlo con precisión.</summary>
    public string? OnvifProfile { get; set; }

    /// <summary>Device Service XAddr exacto publicado por WS-Discovery o detectado por fallback.</summary>
    public string? OnvifDeviceServiceXAddr { get; set; }

    /// <summary>Media Service XAddr anunciado por GetCapabilities.</summary>
    public string? OnvifMediaServiceXAddr { get; set; }

    /// <summary>Imaging Service XAddr anunciado por GetCapabilities.</summary>
    public string? OnvifImagingServiceXAddr { get; set; }

    /// <summary>PTZ Service XAddr anunciado por GetCapabilities.</summary>
    public string? OnvifPtzServiceXAddr { get; set; }

    /// <summary>Events Service XAddr anunciado por GetCapabilities.</summary>
    public string? OnvifEventsServiceXAddr { get; set; }

    /// <summary>Indica si existe Media Service ONVIF.</summary>
    public bool HasOnvifMediaService => !string.IsNullOrWhiteSpace(OnvifMediaServiceXAddr);

    /// <summary>Indica si existe Imaging Service ONVIF.</summary>
    public bool HasOnvifImagingService => !string.IsNullOrWhiteSpace(OnvifImagingServiceXAddr);

    /// <summary>Indica si existe PTZ Service ONVIF.</summary>
    public bool HasOnvifPtzService => !string.IsNullOrWhiteSpace(OnvifPtzServiceXAddr);

    /// <summary>Indica si existe Events Service ONVIF.</summary>
    public bool HasOnvifEventsService => !string.IsNullOrWhiteSpace(OnvifEventsServiceXAddr);

    /// <summary>Indica que se detectó un servicio RTSP utilizable o anunciado.</summary>
    public bool RtspSupported { get; set; }

    /// <summary>Indica que se detectó un servicio HTTP utilizable.</summary>
    public bool HttpSupported { get; set; }

    /// <summary>Indica que se detectó un servicio HTTPS utilizable.</summary>
    public bool HttpsSupported { get; set; }

    /// <summary>Puerto HTTP detectado.</summary>
    public int? HttpPort { get; set; }

    /// <summary>Puerto RTSP detectado.</summary>
    public int? RtspPort { get; set; }

    /// <summary>Primera fecha/hora UTC en la que este dispositivo fue creado en memoria.</summary>
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Última fecha/hora UTC en la que el dispositivo fue visto.</summary>
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Nombre del provider específico asignado más adelante.</summary>
    public string? AssignedProviderName { get; set; }
}
