using System.Net;
using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Calcula el rango completo de IPs host de una subred, a partir de una interfaz de red local.
/// </summary>
public interface ISubnetCalculator
{
    /// <summary>Enumera todas las IPs "host" de la subred (excluye red y broadcast).</summary>
    IEnumerable<IPAddress> GetHostAddresses(NetworkInterfaceInfo networkInterface);
}
