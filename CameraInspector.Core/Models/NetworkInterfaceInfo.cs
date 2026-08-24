using System.Net;

namespace CameraInspector.Core.Models;

/// <summary>
/// Interfaz de red local (Ethernet o Wi-Fi) candidata para escaneo,
/// con la subred ya calculada para no repetir esa cuenta en cada consumidor.
/// </summary>
public sealed class NetworkInterfaceInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IPAddress IpAddress { get; init; }
    public required IPAddress SubnetMask { get; init; }
    public bool IsWireless { get; init; }
    public bool IsUp { get; init; }

    /// <summary>Prefijo CIDR calculado a partir de la máscara (ej. 24 para 255.255.255.0).</summary>
    public int CidrPrefixLength { get; init; }

    public override string ToString() => $"{Name} ({IpAddress}/{CidrPrefixLength})";
}
