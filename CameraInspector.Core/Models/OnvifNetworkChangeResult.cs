namespace CameraInspector.Core.Models;

/// <summary>
/// Resultado de una operación de modificación de red ONVIF.
/// No contiene credenciales ni datos secretos.
/// </summary>
public sealed class OnvifNetworkChangeResult
{
    /// <summary>Indica si el servicio ONVIF aceptó la operación.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Indica si el dispositivo informó que requiere reinicio para activar el cambio.</summary>
    public bool RebootNeeded { get; init; }

    /// <summary>Mensaje técnico para mostrar al operador.</summary>
    public string Message { get; init; } = string.Empty;
}
