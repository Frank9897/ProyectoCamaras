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
    public List<CameraDetectionEvidence> DetectionEvidence { get; } = new();

    public void AddEvidence(string method, double confidence = 0, string? details = null, bool isCameraEvidence = false)
    {
        if (string.IsNullOrWhiteSpace(method)) return;

        var existing = DetectionEvidence.FirstOrDefault(item =>
            item.Method.Equals(method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Details, details, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            DetectionEvidence.Add(new CameraDetectionEvidence
            {
                Method = method.Trim(),
                Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
                Confidence = Math.Clamp(confidence, 0, 1),
                IsCameraEvidence = isCameraEvidence
            });
            return;
        }

        var index = DetectionEvidence.IndexOf(existing);
        DetectionEvidence[index] = existing with
        {
            Confidence = Math.Max(existing.Confidence, Math.Clamp(confidence, 0, 1)),
            IsCameraEvidence = existing.IsCameraEvidence || isCameraEvidence
        };
    }

    public string DetectionReason => DetectionEvidence.Count == 0
        ? "Sin evidencia"
        : string.Join(" + ", DetectionEvidence
            .OrderByDescending(item => item.Confidence)
            .Select(item => item.Method)
            .Distinct(StringComparer.OrdinalIgnoreCase));

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
