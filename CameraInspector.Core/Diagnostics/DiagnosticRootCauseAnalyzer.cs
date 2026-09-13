using CameraInspector.Core.Models;

namespace CameraInspector.Core.Diagnostics;

/// <summary>
/// ETAPA 2 del plan de diagnóstico: cruza los resultados de la batería de pruebas entre sí
/// para producir 1 o más conclusiones en lenguaje de service, en vez de dejar que el técnico
/// interprete 7 filas sueltas a ojo. No reemplaza los DiagnosticResult individuales — los
/// complementa. Lógica pura, sin I/O, para que sea fácil de ajustar y (a futuro) de probar.
/// </summary>
public static class DiagnosticRootCauseAnalyzer
{
    public static IReadOnlyList<DiagnosticConclusion> Analyze(
        DiscoveredDevice device,
        IReadOnlyList<DiagnosticResult> results)
    {
        var conclusions = new List<DiagnosticConclusion>();

        DiagnosticResult? Find(string name) => results.FirstOrDefault(r => r.TestName == name);

        var ping = Find("Ping");
        var http = Find("HTTP");
        var rtspTcp = Find("RTSP TCP");
        var rtspProtocol = Find("RTSP protocolo");
        var onvifDevice = Find("ONVIF Device");
        var onvifNetwork = Find("ONVIF red");

        var isKnownLegacyVendor = IsKnownLegacyVendor(device);

        // Regla 1 — sin ping, pero la cámara YA fue detectada antes (hay evidencia de
        // descubrimiento): más probable un cambio de IP o conflicto de direcciones que un
        // hardware muerto. Distinción importante: no es lo mismo "nunca vista" que
        // "estaba acá y ahora no contesta".
        if (ping is { Success: false } && device.DetectionEvidence.Count > 0)
        {
            conclusions.Add(new DiagnosticConclusion
            {
                Title = "Posible cambio de IP o conflicto de direcciones",
                Severity = DiagnosticSeverity.Critico,
                Explanation = "La cámara no respondió al ping, pero hay evidencia de que fue detectada antes en esta red. Esto sugiere que cambió de dirección IP, está apagada, o hay un conflicto de IP con otro equipo — no necesariamente que el hardware falló.",
                RecommendedAction = "Vuelva a escanear toda la red para localizarla en otra IP antes de asumir que está fuera de servicio."
            });
        }

        // Regla 2 — el equipo contesta al ping (la red y el hardware de red andan), pero
        // ni el servicio web ni el de video responden: apunta al firmware/servicio de la
        // cámara colgado, no a un problema de cableado o switch.
        if (ping is { Success: true } && http is { Success: false } && rtspTcp is { Success: false })
        {
            conclusions.Add(new DiagnosticConclusion
            {
                Title = "El equipo está en la red, pero sus servicios no responden",
                Severity = DiagnosticSeverity.Critico,
                Explanation = "El dispositivo contesta al ping, pero ni el servicio web ni el de video responden. Esto apunta a que el firmware/servicio de la cámara se colgó o está reiniciando, no a un problema de red.",
                RecommendedAction = "Reinicie la cámara (eléctricamente si no responde a un reinicio por software) antes de revisar cableado o switch."
            });
        }

        // Regla 3 — algún servicio pide autenticación. Se distingue "no hay credenciales
        // cargadas" (caso normal en una cámara recién detectada / de fábrica) de
        // "las credenciales guardadas fueron rechazadas" (caso de una cámara ya dada de
        // alta que cambió de clave o fue restablecida) — es justo la distinción que
        // importa para el flujo de service de fábrica vs. preconfigurada.
        var anyAuthRequired = http?.Category == DiagnosticCategory.Autenticacion
                               || rtspProtocol?.Category == DiagnosticCategory.Autenticacion;
        var credentialsWereProvided = onvifNetwork is not null && !onvifNetwork.NotSupported;
        // onvifDevice/onvifNetwork no tienen categoría Autenticación propia (ver Etapa 1);
        // un fallo real (no "NotSupported", no legacy conocido) es la mejor señal indirecta
        // disponible hoy de que las credenciales en uso fueron rechazadas también por ONVIF.
        var onvifAlsoRejected = onvifDevice is { Success: false, NotSupported: false } && !isKnownLegacyVendor;

        if (anyAuthRequired && !credentialsWereProvided)
        {
            conclusions.Add(new DiagnosticConclusion
            {
                Title = "La cámara pide autenticación y no hay credenciales cargadas",
                Severity = DiagnosticSeverity.Advertencia,
                Explanation = "Uno o más servicios exigen usuario y contraseña, pero no se cargó ninguna credencial para esta prueba. Es el caso normal de una cámara recién detectada que todavía no fue dada de alta.",
                RecommendedAction = "Use 'CONFIGURAR ACCESO' para probar el usuario de fábrica del fabricante detectado, o cargue las credenciales si ya las conoce."
            });
        }
        else if (anyAuthRequired && credentialsWereProvided && onvifAlsoRejected)
        {
            conclusions.Add(new DiagnosticConclusion
            {
                Title = "Las credenciales guardadas ya no son válidas",
                Severity = DiagnosticSeverity.Advertencia,
                Explanation = "Se probaron credenciales guardadas, pero más de un servicio las rechazó. Es probable que la cámara haya sido reconfigurada con otra clave, o restablecida a valores de fábrica.",
                RecommendedAction = "Verifique con quien administra la cámara si la contraseña cambió, o pruebe el acceso de fábrica desde 'CONFIGURAR ACCESO' por si fue restablecida."
            });
        }

        // Regla 4 — fabricante legacy conocido sin ONVIF, pero con video y/o web
        // funcionando: comportamiento ESPERADO para esta familia, no un problema.
        if (isKnownLegacyVendor
            && onvifDevice is { Success: false }
            && rtspTcp is { Success: true }
            && (rtspProtocol is { Success: true } || http is { Success: true }))
        {
            var vendorLabel = string.IsNullOrWhiteSpace(device.Manufacturer) ? "Este fabricante" : device.Manufacturer;
            conclusions.Add(new DiagnosticConclusion
            {
                Title = "Cámara legacy funcionando por su protocolo propio (no ONVIF)",
                Severity = DiagnosticSeverity.Info,
                Explanation = $"{vendorLabel} no expone ONVIF real, pero el video y/o el servicio web responden correctamente por su protocolo propio. Es normal para esta familia de cámaras.",
                RecommendedAction = "Use la pestaña de Configuración de Red (CGI/ISAPI específico del fabricante) para administrarla; no dependa de ONVIF para esta cámara."
            });
        }

        // Regla 5 — el puerto RTSP abre pero no habla el protocolo esperado: otro
        // servicio ocupando el puerto, o un firewall/proxy interfiriendo.
        if (rtspTcp is { Success: true } && rtspProtocol is { Success: false })
        {
            conclusions.Add(new DiagnosticConclusion
            {
                Title = "El puerto RTSP está abierto pero no responde como servidor de video",
                Severity = DiagnosticSeverity.Advertencia,
                Explanation = "El puerto TCP de RTSP acepta conexiones, pero el diálogo del protocolo RTSP no se completó correctamente.",
                RecommendedAction = "Verifique en la propia cámara qué puerto tiene configurado para RTSP, y que ningún firewall intercepte ese puerto."
            });
        }

        // Regla 6 — sin ninguna conclusión puntual y sin advertencias/críticos sueltos:
        // se deja constancia explícita de que no hay nada para revisar.
        if (conclusions.Count == 0 && results.All(r => r.Severity != DiagnosticSeverity.Critico && r.Severity != DiagnosticSeverity.Advertencia))
        {
            conclusions.Add(new DiagnosticConclusion
            {
                Title = "Sin hallazgos relevantes",
                Severity = DiagnosticSeverity.Info,
                Explanation = "Todas las pruebas evaluadas respondieron dentro de lo esperado para este dispositivo."
            });
        }

        // Las conclusiones más urgentes van primero (Critico > Advertencia > Info), ya que
        // el enum está declarado en ese orden ascendente.
        return conclusions
            .OrderByDescending(conclusion => conclusion.Severity)
            .ToList();
    }

    /// <summary>Mismo criterio de detección que NetworkConfigurationEditViewModel.DetectLegacyWriter
    /// y CameraDiagnosticService.IsKnownLegacyVendor, duplicado acá porque Core no depende de
    /// las capas de App/Network. Candidato a unificarse en un solo lugar más adelante.</summary>
    private static bool IsKnownLegacyVendor(DiscoveredDevice device)
    {
        var manufacturer = device.Manufacturer ?? string.Empty;
        var model = device.Model ?? string.Empty;

        return manufacturer.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase)
               || model.Contains("IP71", StringComparison.OrdinalIgnoreCase)
               || manufacturer.Contains("Dahua", StringComparison.OrdinalIgnoreCase)
               || manufacturer.Contains("Amcrest", StringComparison.OrdinalIgnoreCase)
               || manufacturer.Contains("Hikvision", StringComparison.OrdinalIgnoreCase)
               || model.StartsWith("DS-", StringComparison.OrdinalIgnoreCase);
    }
}
