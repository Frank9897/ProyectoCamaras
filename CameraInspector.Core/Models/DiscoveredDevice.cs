namespace CameraInspector.Core.Models;

public enum DeviceStatus
{
    Unknown,
    Online,
    Warning,
    Error,
    Offline
}

/// <summary>
/// Dispositivo detectado. ONVIF es solo una de las posibles evidencias de cámara.
/// </summary>
public sealed class DiscoveredDevice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string IpAddress { get; set; }
    public string? MacAddress { get; set; }
    public string? Hostname { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? SerialNumber { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;

    public bool CameraEvidence { get; set; }
    public bool OnvifSupported { get; set; }
    public string? OnvifProfile { get; set; }
    public string? OnvifDeviceServiceXAddr { get; set; }
    public string? OnvifMediaServiceXAddr { get; set; }
    public string? OnvifImagingServiceXAddr { get; set; }
    public string? OnvifPtzServiceXAddr { get; set; }
    public string? OnvifEventsServiceXAddr { get; set; }

    public bool HasOnvifMediaService => !string.IsNullOrWhiteSpace(OnvifMediaServiceXAddr);
    public bool HasOnvifImagingService => !string.IsNullOrWhiteSpace(OnvifImagingServiceXAddr);
    public bool HasOnvifPtzService => !string.IsNullOrWhiteSpace(OnvifPtzServiceXAddr);
    public bool HasOnvifEventsService => !string.IsNullOrWhiteSpace(OnvifEventsServiceXAddr);

    public bool RtspSupported { get; set; }
    public bool HttpSupported { get; set; }
    public bool HttpsSupported { get; set; }
    public int? HttpPort { get; set; }
    public int? RtspPort { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public string? AssignedProviderName { get; set; }
}
