namespace CameraInspector.Core.Models;

/// <summary>
/// Configuración de red expuesta por una cámara mediante ONVIF.
/// Es un modelo de lectura y no contiene credenciales.
/// </summary>
public sealed class OnvifNetworkConfiguration
{
    public List<OnvifNetworkInterfaceInfo> Interfaces { get; init; } = [];
    public List<OnvifNetworkProtocolInfo> Protocols { get; init; } = [];
    public List<string> IPv4Gateways { get; init; } = [];
}

/// <summary>
/// Información básica de una interfaz de red ONVIF.
/// </summary>
public sealed class OnvifNetworkInterfaceInfo
{
    public required string Token { get; init; }
    public bool Enabled { get; init; }
    public string? Name { get; init; }
    public string? HwAddress { get; init; }
    public int? Mtu { get; init; }
    public bool? IPv4Enabled { get; init; }
    public bool? IPv4Dhcp { get; init; }
    public string? IPv4Address { get; init; }
    public int? IPv4PrefixLength { get; init; }
}

/// <summary>
/// Estado de un protocolo de transporte ONVIF.
/// ONVIF define HTTP, HTTPS y RTSP como protocolos de red consultables.
/// </summary>
public sealed class OnvifNetworkProtocolInfo
{
    public required string Name { get; init; }
    public bool Enabled { get; init; }
    public List<int> Ports { get; init; } = [];
}
