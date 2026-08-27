namespace CameraInspector.Core.Models;

/// <summary>
/// Representa un dispositivo local de captura de vídeo expuesto por Windows.
/// Puede corresponder a una webcam UVC, capturadora o cámara virtual.
/// </summary>
public sealed class LocalCameraDevice
{
    /// <summary>Nombre amigable con el que Windows expone la cámara.</summary>
    public required string Name { get; init; }

    /// <summary>Identificador físico de DirectShow cuando está disponible.</summary>
    public string? DevicePath { get; init; }

    /// <summary>Identificador técnico del moniker DirectShow.</summary>
    public string? MonikerString { get; init; }

    /// <summary>Origen que permitió identificar el dispositivo.</summary>
    public string DiscoverySource { get; init; } = "DirectShow";

    /// <summary>Indica si la fuente puede abrirse mediante DirectShow y LibVLC.</summary>
    public bool PreviewSupported { get; init; }

    /// <summary>Transporte inferido a partir de DevicePath o del identificador PnP.</summary>
    public string Transport { get; init; } = "Local/Virtual";

    /// <summary>VID hexadecimal del dispositivo USB, si Windows lo expone.</summary>
    public string? UsbVendorId { get; init; }

    /// <summary>PID hexadecimal del dispositivo USB, si Windows lo expone.</summary>
    public string? UsbProductId { get; init; }

    /// <summary>Indica si Windows lo clasifica como fuente de captura de vídeo.</summary>
    public bool IsVideoCaptureDevice { get; init; } = true;

    /// <summary>Estado actual observado durante la enumeración.</summary>
    public string Status { get; init; } = "Disponible";

    public override string ToString() => Name;
}
