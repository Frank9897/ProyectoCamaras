namespace CameraInspector.Core.Models;

/// <summary>
/// Representa un dispositivo de captura de vídeo local expuesto por Windows.
/// La capa de dominio no necesita conocer DirectShow: solo conserva identidad y capacidades básicas.
/// </summary>
public sealed class LocalCameraDevice
{
    /// <summary>Nombre amigable con el que Windows expone la cámara.</summary>
    public required string Name { get; init; }

    /// <summary>Identificador de dispositivo proporcionado por DirectShow/Windows.</summary>
    public string? DevicePath { get; init; }

    /// <summary>Identificador interno del moniker DirectShow para diagnóstico.</summary>
    public string? MonikerString { get; init; }

    /// <summary>Indica si el dispositivo fue clasificado como cámara de vídeo local.</summary>
    public bool IsVideoCaptureDevice { get; init; } = true;

    /// <summary>Estado actual observado durante la enumeración.</summary>
    public string Status { get; init; } = "Disponible";

    /// <summary>Texto usado directamente por la UI para identificar el dispositivo.</summary>
    public override string ToString() => Name;
}
