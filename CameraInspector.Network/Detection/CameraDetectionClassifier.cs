using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Decide si un dispositivo debe mostrarse como cámara.
/// Una sola señal genérica como RTSP, HTTP, OUI o SSDP nunca alcanza por sí sola.
/// Las detecciones fuertes deben marcar explícitamente que la respuesta constituye
/// evidencia de cámara; esto evita que un protocolo reutilizado por otros servicios
/// sea tratado como identidad de cámara por el solo nombre del detector.
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
        "LegacyCameraHttp",
        "RemoteCameraFingerprint"
    };

    private static readonly HashSet<string> WeakMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "RtspFingerprint",
        "GenericVideoHttp",
        "HttpBanner",
        "OuiMac",
        "SSDP",
        "mDNS",
        "Arp",
        "Ping"
    };

    public static CameraClassificationResult Classify(DiscoveredDevice device)
    {
        var strong = 0;
        var weak = 0;
        var details = new List<string>();

        foreach (var evidence in device.DetectionEvidence)
        {
            var method = evidence.Method?.Trim() ?? string.Empty;
            var isVivotekEvidence = method.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase);

            // El discovery propietario VIVOTEK no se acepta como evidencia fuerte cuando
            // únicamente entrega el fabricante/proveedor. Debe existir identidad independiente.
            if (isVivotekEvidence && !HasIndependentVivotekIdentity(device))
            {
                weak++;
                details.Add($"{evidence.Method} ({evidence.Confidence:P0})");
                continue;
            }

            if (StrongMethods.Contains(method))
            {
                if (evidence.IsCameraEvidence)
                {
                    strong++;
                    details.Add($"{evidence.Method} ({evidence.Confidence:P0})");
                }
                else
                {
                    weak++;
                    details.Add($"{evidence.Method} · no confirma cámara ({evidence.Confidence:P0})");
                }
                continue;
            }

            if (WeakMethods.Contains(method))
            {
                weak++;
                details.Add($"{evidence.Method} ({evidence.Confidence:P0})");
                continue;
            }

            if (evidence.IsCameraEvidence && evidence.Confidence >= 0.9)
            {
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
        var hasCorroboration = strong >= 2 || (strong >= 1 && weak >= 1);
        var hasCameraIdentity = !string.IsNullOrWhiteSpace(device.Model)
            || !string.IsNullOrWhiteSpace(device.SerialNumber)
            || !string.IsNullOrWhiteSpace(device.Manufacturer);

        var isLikelyCamera = device.OnvifSupported
            || device.HasOnvifMediaService
            || device.HasOnvifImagingService
            || device.HasOnvifPtzService
            || hasCorroboration
            || (strong > 0 && device.CameraEvidence && (hasCameraIdentity || !HasOnlyVivotekEvidence(device)));

        return new CameraClassificationResult
        {
            IsLikelyCamera = isLikelyCamera,
            Score = score,
            StrongEvidenceCount = strong,
            WeakEvidenceCount = weak,
            Reason = details.Count == 0 ? "Sin evidencia de cámara" : string.Join(" + ", details.Distinct(StringComparer.OrdinalIgnoreCase))
        };
    }

    private static bool HasIndependentVivotekIdentity(DiscoveredDevice device)
    {
        if (device.OnvifSupported || device.HasOnvifMediaService || device.HasOnvifImagingService || device.HasOnvifPtzService)
            return true;

        return !string.IsNullOrWhiteSpace(device.Model)
            || !string.IsNullOrWhiteSpace(device.SerialNumber);
    }

    private static bool HasOnlyVivotekEvidence(DiscoveredDevice device)
    {
        var meaningful = device.DetectionEvidence.Any(e =>
            !e.Method.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase)
            && !WeakMethods.Contains(e.Method));
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
