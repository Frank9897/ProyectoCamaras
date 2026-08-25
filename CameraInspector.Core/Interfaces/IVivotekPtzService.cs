namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Control propietario PTZ de VIVOTEK.
/// Todas las operaciones modifican físicamente el estado de la cámara y deben partir de una acción explícita del usuario.
/// </summary>
public interface IVivotekPtzService
{
    /// <summary>Ejecuta un movimiento direccional o vuelve a la posición Home.</summary>
    Task<bool> MoveAsync(
        string ipAddress,
        string username,
        string password,
        VivotekPtzMove move,
        CancellationToken cancellationToken = default);

    /// <summary>Detiene cualquier movimiento/acción PTZ activa.</summary>
    Task<bool> StopAsync(
        string ipAddress,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>Activa zoom gran angular.</summary>
    Task<bool> ZoomWideAsync(
        string ipAddress,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>Activa zoom teleobjetivo.</summary>
    Task<bool> ZoomTeleAsync(
        string ipAddress,
        string username,
        string password,
        CancellationToken cancellationToken = default);
}

/// <summary>Movimientos soportados por el CGI PTZ de VIVOTEK.</summary>
public enum VivotekPtzMove
{
    Up,
    Down,
    Left,
    Right,
    Home
}
