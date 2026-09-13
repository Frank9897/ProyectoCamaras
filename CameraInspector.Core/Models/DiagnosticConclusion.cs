namespace CameraInspector.Core.Models;

/// <summary>
/// Conclusión de más alto nivel producida al CRUZAR varios DiagnosticResult entre sí
/// (ver CameraInspector.Core.Diagnostics.DiagnosticRootCauseAnalyzer), en vez de leer cada
/// prueba de forma aislada. Es la diferencia entre "7 filas sueltas" y "esto es lo que
/// realmente está pasando y qué hacer al respecto".
/// </summary>
public sealed record DiagnosticConclusion
{
    /// <summary>Título corto de la conclusión, en lenguaje de service (no técnico crudo).</summary>
    public required string Title { get; init; }

    /// <summary>Qué tan urgente es esta conclusión.</summary>
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Info;

    /// <summary>Por qué se llegó a esta conclusión, explicado para el técnico.</summary>
    public required string Explanation { get; init; }

    /// <summary>Acción concreta sugerida. Null cuando la conclusión es meramente informativa.</summary>
    public string? RecommendedAction { get; init; }
}
