namespace CameraInspector.Core.Models;

/// <summary>
/// Representa un parámetro CGI devuelto por una cámara VIVOTEK.
/// Se mantiene genérico porque los nombres y cantidades de parámetros pueden variar según firmware y modelo.
/// </summary>
public sealed record VivotekParameterItem
{
    /// <summary>Grupo CGI consultado, por ejemplo "system.info" o "image".</summary>
    public required string Group { get; init; }

    /// <summary>Nombre exacto del parámetro reportado por el firmware.</summary>
    public required string Name { get; init; }

    /// <summary>Valor textual devuelto por la cámara.</summary>
    public required string Value { get; init; }
}
