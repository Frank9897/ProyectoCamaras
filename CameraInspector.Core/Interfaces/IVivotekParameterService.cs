using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Consulta grupos de parámetros CGI de VIVOTEK sin modificar la configuración.
/// </summary>
public interface IVivotekParameterService
{
    /// <summary>
    /// Obtiene los parámetros de un grupo utilizando las credenciales proporcionadas explícitamente.
    /// </summary>
    Task<IReadOnlyList<VivotekParameterItem>> GetGroupAsync(
        DiscoveredDevice device,
        string username,
        string password,
        string group,
        CancellationToken cancellationToken = default);
}
