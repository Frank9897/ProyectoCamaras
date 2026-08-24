using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Detecta las interfaces de red disponibles en el equipo (Capa 3 — punto de entrada del pipeline).
/// </summary>
public interface INetworkInterfaceService
{
    IReadOnlyList<NetworkInterfaceInfo> GetActiveInterfaces();
}
