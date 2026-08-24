namespace CameraInspector.Core.Models;

/// <summary>
/// Velocidades normalizadas para un movimiento PTZ continuo.
/// Los valores esperados están en el rango -1 a 1.
/// </summary>
public sealed class OnvifPtzMoveRequest
{
    /// <summary>
    /// Velocidad horizontal: -1 izquierda, 0 detener eje, 1 derecha.
    /// </summary>
    public float Pan { get; init; }

    /// <summary>
    /// Velocidad vertical: -1 abajo, 0 detener eje, 1 arriba.
    /// </summary>
    public float Tilt { get; init; }

    /// <summary>
    /// Velocidad de zoom: -1 alejar, 0 detener zoom, 1 acercar.
    /// </summary>
    public float Zoom { get; init; }
}
