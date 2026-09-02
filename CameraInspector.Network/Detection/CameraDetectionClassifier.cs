using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Decide si un dispositivo debe mostrarse como cámara.
/// Una sola señal genérica como RTSP, HTTP, OUI o SSDP nunca alcanza por sí sola.
/// </summary>
public static class CameraDetectionClassifier
{
    private static readonly HashSet<string> StrongMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "OnvifProbe",
        "WsDiscoveryOnvif",
        "VIVOTEK",
        "VivotekDiscovery",
        "HikvisionSADP",
        "DahuaDHIP",
        "AxisMdns",
        "Hanwha",
        "Uniview",
        "MOBOTIX",
        "Reolink",
        "LegacyCameraHttp"
    };

    private static readonly HashSet<string> WeakMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "RtspFingerprint",
        "GenericVideoHttp",
        "HttpBanner",
        "OuiMac",
        "SSDP",
        "mDNS",
        "Arp"
    };

    public static CameraClassificationResult Classify(DiscoveredDevice device)
    {
        var strong = 0;
        var weak = 0;
        var details = new List<string>();

        foreach (var evidence in device.DetectionEvidence)
        {
            if (StrongMethods.Contains(evidence.Method))
            {
                strong++;
                details.Add($"{evidence.Method} ({evidence.Confidence:P0})");
                continue;
            }

            if (WeakMethods.Contains(evidence.Method))
            {
                weak++;
                details.Add($"{evidence.Method} ({evidence.Confidence:P0})");
                continue;
            }

            if (evidence.IsCameraEvidence && evidence.Confidence >= 0.9)
            {
                // Evidencia no genérica: solo suma como fuerte cuando el propio detector la marcó explícitamente.
                strong++;
                details.Add($"{evidence.Method} ({evidence.Confidence:P0})");
            }
            else if (evidence.IsCameraEvidence)
            {
                weak++;
                details.Add($"{evidence.Method} ({evidence.Confidence:P0})");
            }
        }

        if (device.OnvifSupported)
            strong++;
        if (device.HasOnvifMediaService)
            strong++;
        if (device.HasOnvifImagingService)
            strong++;
        if (device.HasOnvifPtzService)
            strong++;

        var score = Math.Min(100, strong * 40 + Math.Min(20, weak * 5));
        var hasStrongEvidence = strong > 0;
        var hasCorroboration = strong >= 2 || (strong >= 1 && weak >= 1);

        // ONVIF o un detector propietario fuerte bastan. Señales genéricas nunca bastan.
        var isLikelyCamera = device.OnvifSupported
            || device.HasOnvifMediaService
            || device.HasOnvifImagingService
            || device.HasOnvifPtzService
            || hasCorroboration
            || (hasStrongEvidence && device.CameraEvidence && !IsGenericOnly(device));

        return new CameraClassificationResult
        {
            IsLikelyCamera = isLikelyCamera,
            Score = score,
            StrongEvidenceCount = strong,
            WeakEvidenceCount = weak,
            Reason = details.Count == 0 ? "Sin evidencia de cámara" : string.Join(" + ", details.Distinct(StringComparer.OrdinalIgnoreCase))
        };
    }

    private static bool IsGenericOnly(DiscoveredDevice device)
    {
        var meaningful = device.DetectionEvidence.Any(e =>
            !WeakMethods.Contains(e.Method) &&
            !string.Equals(e.Method, "RtspFingerprint", StringComparison.OrdinalIgnoreCase));
        return !meaningful;
    }
}

public sealed record CameraClassificationResult
{
    public bool IsLikelyCamera { get; init; }
    public int Score { get; init; }
    public int StrongEvidenceCount { get; init; }
    public int WeakEvidenceCount { get; init; }
    public string Reason { get; init; } = string.Empty;
}
