namespace CameraInspector.Core.Models;

/// <summary>
/// Representa una fila del historial de pruebas de una cámara.
/// Es un modelo de lectura independiente de EF Core para que la UI no conozca la base de datos.
/// </summary>
public sealed record DiagnosticHistoryItem
{
    /// <summary>Identificador SQLite del registro histórico.</summary>
    public required int Id { get; init; }

    /// <summary>Nombre de la prueba ejecutada, por ejemplo Ping, ONVIF o RTSP.</summary>
    public required string TestName { get; init; }

    /// <summary>Resultado persistido: OK, ERROR o SKIPPED.</summary>
    public required string Result { get; init; }

    /// <summary>Duración de la prueba en milisegundos cuando fue medida.</summary>
    public int? ResponseTimeMs { get; init; }

    /// <summary>Mensaje técnico devuelto por la prueba.</summary>
    public string? Message { get; init; }

    /// <summary>Momento UTC en el que se ejecutó la prueba.</summary>
    public required DateTimeOffset TestDate { get; init; }
}
