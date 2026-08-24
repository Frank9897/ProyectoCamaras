using System.Net;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Resuelve direcciones MAC a partir de la tabla ARP del sistema operativo.
/// En Windows esto se apoya en la tabla ARP del kernel (poblada por el propio
/// tráfico de ping/TCP previo), vía iphlpapi.
/// </summary>
public interface IArpResolver
{
    /// <summary>Devuelve un diccionario IP → MAC (en formato AA:BB:CC:DD:EE:FF) para las IPs conocidas por el SO.</summary>
    IReadOnlyDictionary<IPAddress, string> GetArpTable();
}
