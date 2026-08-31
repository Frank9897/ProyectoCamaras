namespace CameraInspector.Core.Models;

/// <summary>
/// Modo de captura que una cámara local consigue abrir correctamente.
/// </summary>
public sealed class LocalCameraCapability
{
    /// <summary>Ancho real del modo de captura.</summary>
    public required int Width { get; init; }

    /// <summary>Alto real del modo de captura.</summary>
    public required int Height { get; init; }

    /// <summary>FPS del modo observado/negociado por el backend.</summary>
    public required double Fps { get; init; }

    /// <summary>Backend utilizado para validar el modo.</summary>
    public required string Backend { get; init; }

    /// <summary>Formato solicitado durante la validación, por ejemplo MJPG o Nativo.</summary>
    public required string Format { get; init; }

    public string DisplayName => $"{Width}x{Height} · {Fps:0.#} FPS · {Format}";

    public override string ToString() => DisplayName;
}
