namespace CameraInspector.Core.Models;

/// <summary>
/// Evidencia devuelta por un detector. Las evidencias se acumulan en el dispositivo.
/// </summary>
public sealed record ManufacturerDetectionResult
{
    public required string DetectorName { get; init; }
    public required double Confidence { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? SerialNumber { get; init; }
    public bool CameraEvidence { get; init; }
    public string? EvidenceDetails { get; init; }
    public bool OnvifSupported { get; init; }
    public string? OnvifProfile { get; init; }
    public string? OnvifDeviceServiceXAddr { get; init; }
    public bool RtspSupported { get; init; }
    public bool HttpSupported { get; init; }
    public bool HttpsSupported { get; init; }
    public int? HttpPort { get; init; }
    public int? RtspPort { get; init; }
}
