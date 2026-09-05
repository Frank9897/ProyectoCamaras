namespace CameraInspector.Core.Models;

/// <summary>
/// Alerta persistente presentada por el centro de alertas.
/// Se alimenta de eventos de SQLite sin exponer entidades de persistencia a la UI.
/// </summary>
public sealed record AlertItem
{
    public required int Id { get; init; }
    public required int CameraId { get; init; }
    public required string IpAddress { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public required string Severity { get; init; }
    public required string Type { get; init; }
    public required string Description { get; init; }
    public required DateTimeOffset Date { get; init; }
}
