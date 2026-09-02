namespace CameraInspector.Core.Models;

/// <summary>
/// Evidencia individual que explica por qué un dispositivo fue identificado como cámara.
/// </summary>
public sealed record CameraDetectionEvidence
{
    public required string Method { get; init; }
    public string? Details { get; init; }
    public double Confidence { get; init; }
    public bool IsCameraEvidence { get; init; }
}
