namespace CameraInspector.Core.Models;

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

    /// <summary>Momento UTC en el que finalizó la prueba.</summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}
