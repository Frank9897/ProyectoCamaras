namespace CameraInspector.Core.Models;

/// <summary>
/// Resultado de un intento de detección de fabricante/capacidades por parte de UN detector
/// (Capa 4). El ManufacturerResolver junta los resultados de todos los detectores registrados
/// y se queda con el de mayor Confidence — así un detector débil (OUI) nunca pisa a uno
/// fuerte (respuesta ONVIF real), pero tampoco hace falta que todos coincidan.
/// </summary>
public sealed record ManufacturerDetectionResult
{
    public required string DetectorName { get; init; }

    /// <summary>0.0 a 1.0. Ej: OUI ~0.4 (ambiguo), banner HTTP ~0.7, respuesta ONVIF real ~0.95.</summary>
    public required double Confidence { get; init; }

    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? SerialNumber { get; init; }

    public bool OnvifSupported { get; init; }
    public string? OnvifProfile { get; init; }

    /// <summary>
    /// URL exacta del Device Service ONVIF utilizada por el detector.
    /// Se conserva para que las capas posteriores reutilicen el endpoint real detectado
    /// en lugar de reconstruirlo a partir de la IP.
    /// </summary>
    public string? OnvifDeviceServiceXAddr { get; init; }

    public bool RtspSupported { get; init; }
    public bool HttpSupported { get; init; }
    public bool HttpsSupported { get; init; }
    public int? HttpPort { get; init; }
    public int? RtspPort { get; init; }
}
