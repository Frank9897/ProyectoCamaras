namespace CameraInspector.Core.Models;

/// <summary>
/// Información básica que un dispositivo devuelve mediante GetDeviceInformation.
/// Este modelo representa datos de identidad del equipo y no depende de WPF ni de la persistencia.
/// </summary>
public sealed record OnvifDeviceInformation
{
    /// <summary>Fabricante declarado por el propio dispositivo ONVIF.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Modelo declarado por el propio dispositivo ONVIF.</summary>
    public string? Model { get; init; }

    /// <summary>Versión de firmware declarada por el dispositivo.</summary>
    public string? FirmwareVersion { get; init; }

    /// <summary>Número de serie declarado por el dispositivo.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>Identificador hardware adicional cuando el firmware lo proporciona.</summary>
    public string? HardwareId { get; init; }
}
