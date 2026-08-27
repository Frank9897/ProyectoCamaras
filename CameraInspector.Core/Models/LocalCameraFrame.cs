namespace CameraInspector.Core.Models;

/// <summary>
/// Representa un frame de vídeo local listo para ser mostrado por la interfaz WPF.
/// Los píxeles se entregan en BGRA32 para evitar que Core conozca tipos gráficos de WPF.
/// </summary>
public sealed class LocalCameraFrame
{
    /// <summary>Buffer BGRA32. Cada píxel ocupa cuatro bytes: azul, verde, rojo y alfa.</summary>
    public required byte[] Pixels { get; init; }

    /// <summary>Ancho del frame en píxeles.</summary>
    public required int Width { get; init; }

    /// <summary>Alto del frame en píxeles.</summary>
    public required int Height { get; init; }

    /// <summary>Número de bytes de una fila completa del buffer BGRA32.</summary>
    public required int Stride { get; init; }

    /// <summary>Instante UTC en el que se capturó el frame.</summary>
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
