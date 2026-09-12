namespace CameraInspector.Core.Models;

/// <summary>Qué tan urgente es un resultado de diagnóstico para el técnico.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Prueba exitosa, o fallo esperado/no aplicable (ver NotSupported) — no requiere acción.</summary>
    Info,

    /// <summary>Algo no funciona del todo, pero otras vías (video, configuración) pueden seguir
    /// disponibles. Vale la pena revisarlo, no es necesariamente bloqueante.</summary>
    Advertencia,

    /// <summary>Bloquea el objetivo principal de la prueba (sin esto, no hay comunicación o no
    /// hay video posible). Requiere atención antes de continuar con el service.</summary>
    Critico
}

/// <summary>A qué área del problema pertenece la prueba, para agrupar en la UI y en el
/// futuro motor de causa raíz.</summary>
public enum DiagnosticCategory
{
    Red,
    Autenticacion,
    Video,
    Configuracion
}

/// <summary>
/// Resultado de una prueba individual realizada sobre un dispositivo.
/// </summary>
public sealed record DiagnosticResult
{
    /// <summary>Nombre de la prueba que se ejecutó.</summary>
    public required string TestName { get; init; }

    /// <summary>Indica si la prueba se completó correctamente.</summary>
    public bool Success { get; init; }

    /// <summary>
    /// Indica que la capacidad no es aplicable o no está soportada por el dispositivo.
    /// Se diferencia de Success=false porque "no soportado" no es necesariamente un fallo.
    /// </summary>
    public bool NotSupported { get; init; }

    /// <summary>Tiempo aproximado empleado por la prueba, cuando puede medirse.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Mensaje técnico corto para mostrar al técnico.</summary>
    public string? Message { get; init; }

    /// <summary>Qué tan urgente es este resultado. Por defecto se deriva de Success/NotSupported
    /// si la prueba no fija un valor propio (ver DiagnosticResultExtensions.WithDefaultSeverity).</summary>
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Info;

    /// <summary>Área del problema a la que pertenece esta prueba.</summary>
    public DiagnosticCategory Category { get; init; } = DiagnosticCategory.Red;

    /// <summary>
    /// Sugerencia concreta de qué hacer, en lenguaje de service — no un mensaje técnico
    /// crudo. Null cuando la prueba fue exitosa y no hay nada que recomendar.
    /// </summary>
    public string? RecommendedAction { get; init; }

    /// <summary>Momento UTC en el que finalizó la prueba.</summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}
