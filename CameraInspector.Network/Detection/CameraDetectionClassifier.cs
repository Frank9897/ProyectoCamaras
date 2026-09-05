using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Decide si un dispositivo debe mostrarse como cámara.
/// Las señales genéricas de red nunca deben convertir por sí solas a un router,
/// access point, extensor u otro equipo de infraestructura en una cámara.
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
            var normalizedMethod = method;
            var isVivotekEvidence = method.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase);

            // Una respuesta 401/403 a un endpoint propietario no demuestra que el equipo
            // sea de ese fabricante: routers, APs y otros servidores también pueden devolverla.
            if (IsAuthenticationOnlyEvidence(evidence))
            {
                weak++;
                details.Add($"{method} · autenticación sin identidad ({evidence.Confidence:P0})");
                continue;
            }

            // El discovery propietario VIVOTEK no alcanza por sí solo si únicamente informa
            // fabricante/proveedor. Se requiere identidad independiente.
            if (isVivotekEvidence && !HasIndependentVivotekIdentity(device))
            {
                weak++;
                details.Add($"{method} ({evidence.Confidence:P0})");
                continue;
            }

            if (StrongMethods.Contains(normalizedMethod))
            {
                if (evidence.IsCameraEvidence)
                {
                    strong++;
                    details.Add($"{method} ({evidence.Confidence:P0})");
                }
                else
                {
                    weak++;
                    details.Add($"{method} · no confirma cámara ({evidence.Confidence:P0})");
                }
                continue;
            }

            // LegacyHttp solo es fuerte cuando hubo una respuesta de identidad real.
            // Las respuestas de autenticación quedan deliberadamente como evidencia débil.
            if (method.StartsWith("LegacyHttp:", StringComparison.OrdinalIgnoreCase))
            {
                if (evidence.IsCameraEvidence && evidence.Confidence >= 0.99)
                {
                    strong++;
                    details.Add($"{method} ({evidence.Confidence:P0})");
                }
                else
                {
                    weak++;
                    details.Add($"{method} · identidad insuficiente ({evidence.Confidence:P0})");
                }
                continue;
            }

            if (WeakMethods.Contains(normalizedMethod))
            {
                weak++;
                details.Add($"{method} ({evidence.Confidence:P0})");
                continue;
            }

            if (evidence.IsCameraEvidence && evidence.Confidence >= 0.9)
            {
                strong++;
                details.Add($"{method} ({evidence.Confidence:P0})");
            }
            else if (evidence.IsCameraEvidence)
            {
                weak++;
                details.Add($"{method} ({evidence.Confidence:P0})");
            }
        }

        // Estas propiedades solo representan evidencia fuerte cuando realmente proceden de
        // ONVIF confirmado. El resolver ya no las eleva por una respuesta 401/403.
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

        // El fabricante por OUI/banner no es identidad suficiente. La identidad útil debe
        // venir acompañada de una evidencia específica de cámara.
        var hasCameraIdentity = !string.IsNullOrWhiteSpace(device.Model)
            || !string.IsNullOrWhiteSpace(device.SerialNumber);

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

    private static bool IsAuthenticationOnlyEvidence(CameraDetectionEvidence evidence)
    {
        var details = evidence.Details ?? string.Empty;
        return details.Contains("401", StringComparison.OrdinalIgnoreCase)
            || details.Contains("403", StringComparison.OrdinalIgnoreCase)
            || details.Contains("autentic", StringComparison.OrdinalIgnoreCase)
            || details.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || details.Contains("requiere acceso", StringComparison.OrdinalIgnoreCase);
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
