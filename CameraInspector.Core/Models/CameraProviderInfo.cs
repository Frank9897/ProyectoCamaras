namespace CameraInspector.Core.Models;

/// <summary>
/// Información común devuelta por un provider propietario.
/// Complementa los datos ONVIF sin sustituir la identidad principal del inventario.
/// </summary>
public sealed record CameraProviderInfo
{
    /// <summary>Nombre comercial del provider/protocolo que respondió.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Fabricante reportado por el protocolo propietario.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Modelo reportado por el protocolo propietario.</summary>
    public string? Model { get; init; }

    /// <summary>Versión de firmware reportada por el protocolo propietario.</summary>
    public string? FirmwareVersion { get; init; }

    /// <summary>Número de serie reportado por el protocolo propietario.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>MAC reportada por el protocolo propietario.</summary>
    public string? MacAddress { get; init; }

    /// <summary>Tipo de dispositivo propietario, cuando el fabricante lo expone.</summary>
    public string? DeviceType { get; init; }
}
